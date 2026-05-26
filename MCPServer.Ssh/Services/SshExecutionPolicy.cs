using LanguageExt;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;
using Microsoft.Extensions.Options;

namespace MCPServer.Ssh.Services;

public sealed class SshExecutionPolicy : ISshExecutionPolicy
{
    private static readonly string[] InlineCommandSwitches =
    [
        "-c",
        "--command",
        "-command",
        "/c"
    ];

    private readonly IOptionsMonitor<SshToolSettings> _settings;
    private readonly ISshProfileStore _profileStore;

    public SshExecutionPolicy(IOptionsMonitor<SshToolSettings> settings, ISshProfileStore profileStore)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
    }

    public async ValueTask<Fin<SshExecutionPolicyDecision>> EvaluateAsync(SshExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var settings = SshToolSettings.Normalize(_settings.CurrentValue);

        if (!settings.Enabled)
        {
            return Succeed(Denied("SSH tools are disabled by configuration."));
        }

        if (string.IsNullOrWhiteSpace(request.Profile))
        {
            return Succeed(Denied("profile is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return Succeed(Denied("command is required."));
        }

        var command = request.Command.Trim();
        if (ContainsPathSeparator(command))
        {
            return Succeed(Denied("command must be an executable name, not a path."));
        }

        if (command.Contains(' ', StringComparison.Ordinal) || command.Contains('\t', StringComparison.Ordinal))
        {
            return Succeed(Denied("command must be a single executable name. Pass arguments through the arguments array."));
        }

        var profileName = request.Profile.Trim();
        var catalogResult = await _profileStore.LoadProfilesAsync(cancellationToken).ConfigureAwait(false);
        if (catalogResult.IsFail)
        {
            return catalogResult.Match<Fin<SshExecutionPolicyDecision>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH profile catalog success while handling failure."),
                Fail: error => error);
        }

        var catalog = catalogResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH profile catalog failure while handling success."));

        if (!catalog.Profiles.TryGetValue(profileName, out var profile))
        {
            return Succeed(Denied($"Unknown SSH profile '{profileName}'. Add it to the SQLite SSH profile database via the sidecar profile commands or configure McpTools:Ssh:ProfileDatabasePath."));
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            return Succeed(Denied($"SSH profile '{profileName}' is missing host."));
        }

        if (string.IsNullOrWhiteSpace(profile.Username))
        {
            return Succeed(Denied($"SSH profile '{profileName}' is missing username."));
        }

        var rootDecision = EvaluateRootUserProfile(profileName, profile, settings);
        if (!rootDecision.Allowed)
        {
            return Succeed(rootDecision);
        }

        var normalizedCommand = NormalizeCommandName(command);
        var arguments = NormalizeStrings(request.Arguments);
        var allowAllCommands = profile.AllowAllCommands;
        var workingDirectory = request.WorkingDirectory is { Length: > 0 } requestedWorkingDirectory
            ? requestedWorkingDirectory.Trim()
            : profile.WorkingDirectory ?? string.Empty;
        var timeoutSeconds = request.TimeoutSeconds is { } requestedTimeoutSeconds
            ? Math.Clamp(requestedTimeoutSeconds, 1, Math.Max(1, settings.TimeoutSeconds))
            : settings.TimeoutSeconds;
        var port = profile.Port is > 0 ? profile.Port : 22;
        var sudoAllowed = IsSudoCommand(normalizedCommand) && allowAllCommands;
        var deniedCommands = BuildMergedStrings(settings.DeniedCommands, profile.DeniedCommands);

        if (!allowAllCommands && !sudoAllowed && IsDeniedCommand(normalizedCommand, deniedCommands))
        {
            return Succeed(Denied($"Command '{command}' is explicitly denied."));
        }

        if (allowAllCommands)
        {
            var privilegedDecision = EvaluatePrivilegedProfile(profileName, profile, settings);
            if (!privilegedDecision.Allowed)
            {
                return Succeed(privilegedDecision);
            }
        }
        else
        {
            IEnumerable<string> allowedCommands = profile.AllowedCommands.Count > 0
                ? profile.AllowedCommands
                : settings.AllowedCommands;

            if (settings.RequireExplicitProfileAllowlist && !IsAllowedCommand(normalizedCommand, allowedCommands) && !sudoAllowed)
            {
                return Succeed(Denied($"Command '{command}' is not allowed for SSH profile '{profileName}'."));
            }
        }

        if (!settings.AllowShellInterpreterInlineCommands && !IsAllowedRootOverride(profile) && IsShellInterpreter(normalizedCommand) && ContainsInlineCommand(arguments))
        {
            return Succeed(Denied($"Command '{command}' cannot use inline command switches until argument-aware SSH shell policy is enabled or the profile explicitly enables allowedRoot with privileged allow-all execution."));
        }

        if (!IsAllowedRemoteWorkingDirectory(workingDirectory, profile.AllowedRemotePathPrefixes))
        {
            return Succeed(Denied($"Remote working directory '{workingDirectory}' is not allowed for SSH profile '{profileName}'."));
        }

        return Succeed(new SshExecutionPolicyDecision
        {
            Allowed = true,
            Decision = "allowed",
            ProfileName = profileName,
            Host = profile.Host.Trim(),
            Port = port,
            Username = profile.Username.Trim(),
            ResolvedCommand = command,
            ResolvedArguments = arguments,
            WorkingDirectory = workingDirectory,
            TimeoutSeconds = timeoutSeconds,
            MaxOutputChars = Math.Max(1, settings.MaxOutputChars),
            PrivateKeyPath = profile.PrivateKeyPath,
            PrivateKeyPassphraseEnvironmentVariable = profile.PrivateKeyPassphraseEnvironmentVariable,
            PasswordEnvironmentVariable = profile.PasswordEnvironmentVariable,
            HostKeySha256 = profile.HostKeySha256,
            AcceptUnknownHostKey = profile.AcceptUnknownHostKey || settings.AllowUnknownHostKeys
        });
    }

    private static bool IsAllowedRootOverride(SshProfileDefinition profile)
    {
        return profile.Username.Equals("root", StringComparison.OrdinalIgnoreCase) &&
            profile.AllowedRoot &&
            profile.Privileged &&
            profile.AllowAllCommands &&
            !string.IsNullOrWhiteSpace(profile.HostKeySha256) &&
            !profile.AcceptUnknownHostKey;
    }

    private static SshExecutionPolicyDecision EvaluateRootUserProfile(string profileName, SshProfileDefinition profile, SshToolSettings settings)
    {
        if (!profile.Username.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            return new SshExecutionPolicyDecision
            {
                Allowed = true,
                Decision = "allowed"
            };
        }

        if (!profile.AllowedRoot)
        {
            return Denied($"SSH profile '{profileName}' uses username 'root' but allowedRoot is not enabled. Root SSH plus AI automation can seriously damage systems, delete data, or take down hosts. Enable allowedRoot only for explicitly isolated, pinned, audited targets.");
        }

        if (!profile.Privileged)
        {
            return Denied($"SSH profile '{profileName}' enables allowedRoot but is not marked privileged. Root access requires both allowedRoot=true and privileged=true.");
        }

        if (string.IsNullOrWhiteSpace(profile.HostKeySha256) || profile.AcceptUnknownHostKey || settings.AllowUnknownHostKeys)
        {
            return Denied($"SSH profile '{profileName}' enables allowedRoot and must pin a host key. Unknown host keys are forbidden for root SSH automation.");
        }

        return new SshExecutionPolicyDecision
        {
            Allowed = true,
            Decision = "allowed"
        };
    }

    private static SshExecutionPolicyDecision EvaluatePrivilegedProfile(string profileName, SshProfileDefinition profile, SshToolSettings settings)
    {
        if (!profile.Privileged)
        {
            return Denied($"SSH profile '{profileName}' must be marked privileged before allow-all command execution is allowed.");
        }

        if (string.IsNullOrWhiteSpace(profile.HostKeySha256) || profile.AcceptUnknownHostKey || settings.AllowUnknownHostKeys)
        {
            return Denied($"SSH profile '{profileName}' must pin a host key before allow-all command execution is allowed.");
        }

        return new SshExecutionPolicyDecision
        {
            Allowed = true,
            Decision = "allowed"
        };
    }

    private static bool IsAllowedRemoteWorkingDirectory(string workingDirectory, IEnumerable<string> allowedPrefixes)
    {
        var prefixes = NormalizeStrings(allowedPrefixes);
        if (prefixes is [] || string.IsNullOrWhiteSpace(workingDirectory))
        {
            return true;
        }

        var normalized = NormalizeRemotePath(workingDirectory);
        foreach (var prefix in prefixes.Select(NormalizeRemotePath))
        {
            if (normalized.Equals(prefix, StringComparison.Ordinal) ||
                normalized.StartsWith(prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeRemotePath(string path)
    {
        var replaced = path.Trim().Replace('\\', '/');
        while (replaced.Contains("//", StringComparison.Ordinal))
        {
            replaced = replaced.Replace("//", "/", StringComparison.Ordinal);
        }

        return replaced.TrimEnd('/');
    }

    private static bool ContainsPathSeparator(string command)
    {
        return command.Contains('/', StringComparison.Ordinal) || command.Contains('\\', StringComparison.Ordinal);
    }

    private static string NormalizeCommandName(string command)
    {
        var fileName = command.Trim();
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
    }

    private static bool IsAllowedCommand(string normalizedCommand, IEnumerable<string> allowedCommands)
    {
        foreach (var allowed in NormalizeStrings(allowedCommands))
        {
            if (normalizedCommand.Equals(NormalizeCommandName(allowed), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeniedCommand(string normalizedCommand, IEnumerable<string> deniedCommands)
    {
        foreach (var denied in NormalizeStrings(deniedCommands))
        {
            if (normalizedCommand.Equals(NormalizeCommandName(denied), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShellInterpreter(string normalizedCommand)
    {
        return normalizedCommand is "bash" or "sh" or "zsh" or "fish" or "cmd" or "powershell" or "pwsh";
    }

    private static bool ContainsInlineCommand(IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            foreach (var inlineSwitch in InlineCommandSwitches)
            {
                if (argument.Equals(inlineSwitch, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSudoCommand(string normalizedCommand)
    {
        return normalizedCommand.Equals("sudo", StringComparison.OrdinalIgnoreCase);
    }

    private static SshExecutionPolicyDecision Denied(string reason)
    {
        return new SshExecutionPolicyDecision
        {
            Allowed = false,
            Decision = "denied",
            Reason = reason
        };
    }

    private static Fin<SshExecutionPolicyDecision> Succeed(SshExecutionPolicyDecision decision)
    {
        return Fin.Succ<SshExecutionPolicyDecision>(decision);
    }

    private static string[] NormalizeStrings(IEnumerable<string> values)
    {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildMergedStrings(IEnumerable<string> defaults, IEnumerable<string> overrides)
    {
        return defaults
            .Concat(overrides)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
