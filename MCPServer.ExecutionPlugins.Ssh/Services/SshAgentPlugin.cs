using System.Globalization;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Execution.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Domain.Capabilities;
using MCPServer.AgentRouter.Domain.Policies;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;

namespace MCPServer.ExecutionPlugins.Ssh.Services;

public sealed class SshAgentPlugin : IAgentPlugin
{
    public const string PluginName = "ssh";

    private static readonly IReadOnlyList<AgentCapabilityDescriptor> SupportedCapabilities =
    [
        AgentCapabilityDescriptor.Create(
            AgentRouterSshCapabilityNames.RemoteShell,
            "Remote shell over SSH",
            AgentExecutionRiskLevels.Critical,
            requiresApproval: true,
            metadata: new Dictionary<string, string?>
            {
                ["plugin"] = PluginName,
                ["execution"] = "existing-ssh-agent-runtime"
            }),
        AgentCapabilityDescriptor.Create(
            AgentRouterSshCapabilityNames.SshAgent,
            "SSH agent command sequence",
            AgentExecutionRiskLevels.Critical,
            requiresApproval: true,
            metadata: new Dictionary<string, string?>
            {
                ["plugin"] = PluginName,
                ["execution"] = "existing-ssh-agent-runtime"
            })
    ];

    private readonly ISshAgentRuntime _runtime;

    public SshAgentPlugin(ISshAgentRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public string Name => PluginName;

    public IReadOnlyList<AgentCapabilityDescriptor> Capabilities => SupportedCapabilities;

    public bool CanHandle(AgentPluginExecutionRequest request)
    {
        return string.Equals(request.CapabilityName, AgentRouterSshCapabilityNames.RemoteShell, StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.CapabilityName, AgentRouterSshCapabilityNames.SshAgent, StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask<Fin<AgentPluginExecutionResult>> ExecuteAsync(
        AgentPluginExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var launchRequest = BuildLaunchRequest(request);
        if (launchRequest.IsFail)
        {
            return launchRequest.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH launch request success while handling failure."),
                Fail: error => Fin.Fail<AgentPluginExecutionResult>(error));
        }

        var sshRequest = launchRequest.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH launch request failure while handling success."));

        var launch = await _runtime.LaunchAsync(sshRequest, cancellationToken).ConfigureAwait(false);
        return launch.Match(
            Succ: response => Fin.Succ(ToPluginResult(response)),
            Fail: error => Fin.Fail<AgentPluginExecutionResult>(error));
    }

    private static Fin<SshAgentLaunchRequest> BuildLaunchRequest(AgentPluginExecutionRequest request)
    {
        var parameters = request.ParametersOrEmpty;
        var profile = GetValue(parameters, AgentRouterSshMetadataKeys.Profile, "profile");
        if (string.IsNullOrWhiteSpace(profile))
        {
            return Fin.Fail<SshAgentLaunchRequest>(Error.New("SSH profile is required. Use metadata key 'ssh.profile'."));
        }

        if (request.Objective.IsEmpty)
        {
            return Fin.Fail<SshAgentLaunchRequest>(Error.New("Agent objective is required for SSH agent execution."));
        }

        var commands = BuildCommands(parameters);
        if (commands.IsFail)
        {
            return commands.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH command parsing success while handling failure."),
                Fail: error => Fin.Fail<SshAgentLaunchRequest>(error));
        }

        var parsedCommands = commands.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH command parsing failure while handling success."));

        return Fin.Succ(new SshAgentLaunchRequest
        {
            Profile = profile.Trim(),
            Objective = request.Objective.ToString(),
            WorkingDirectory = GetValue(parameters, AgentRouterSshMetadataKeys.WorkingDirectory, "workingDirectory"),
            TimeoutSecondsPerStep = TryGetInt32(parameters, AgentRouterSshMetadataKeys.TimeoutSecondsPerStep, "timeoutSecondsPerStep"),
            OperationKey = request.RunId.IsEmpty ? null : request.RunId.ToString(),
            Commands = parsedCommands
        });
    }

    private static Fin<IReadOnlyList<SshAgentCommandRequest>> BuildCommands(IReadOnlyDictionary<string, string?> parameters)
    {
        var commandsJson = GetValue(parameters, AgentRouterSshMetadataKeys.CommandsJson, "commandsJson");
        if (!string.IsNullOrWhiteSpace(commandsJson))
        {
            return ParseCommandsJson(commandsJson);
        }

        var command = GetValue(parameters, AgentRouterSshMetadataKeys.Command, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return Fin.Fail<IReadOnlyList<SshAgentCommandRequest>>(Error.New("SSH command is required. Use 'ssh.command' or 'ssh.commandsJson'."));
        }

        var args = ParseArgumentsJson(GetValue(parameters, AgentRouterSshMetadataKeys.ArgumentsJson, "argumentsJson"));
        if (args.IsFail)
        {
            return args.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH argument parsing success while handling failure."),
                Fail: error => Fin.Fail<IReadOnlyList<SshAgentCommandRequest>>(error));
        }

        var parsedArgs = args.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH argument parsing failure while handling success."));

        return Fin.Succ<IReadOnlyList<SshAgentCommandRequest>>(
        [
            new SshAgentCommandRequest
            {
                Command = command.Trim(),
                Arguments = parsedArgs,
                WorkingDirectory = GetValue(parameters, AgentRouterSshMetadataKeys.WorkingDirectory, "workingDirectory"),
                TimeoutSeconds = TryGetInt32(parameters, "ssh.timeoutSeconds", "timeoutSeconds")
            }
        ]);
    }

    private static Fin<IReadOnlyList<SshAgentCommandRequest>> ParseCommandsJson(string commandsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(commandsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Fin.Fail<IReadOnlyList<SshAgentCommandRequest>>(Error.New("ssh.commandsJson must be a JSON array."));
            }

            var commands = new List<SshAgentCommandRequest>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return Fin.Fail<IReadOnlyList<SshAgentCommandRequest>>(Error.New("Each SSH command entry must be a JSON object."));
                }

                var command = TryGetString(item, "command");
                if (string.IsNullOrWhiteSpace(command))
                {
                    return Fin.Fail<IReadOnlyList<SshAgentCommandRequest>>(Error.New("Each SSH command entry requires a command property."));
                }

                commands.Add(new SshAgentCommandRequest
                {
                    Command = command.Trim(),
                    Arguments = ReadStringArray(item, "arguments"),
                    WorkingDirectory = TryGetString(item, "workingDirectory"),
                    TimeoutSeconds = TryGetInt32(item, "timeoutSeconds")
                });
            }

            if (commands.Count == 0)
            {
                return Fin.Fail<IReadOnlyList<SshAgentCommandRequest>>(Error.New("ssh.commandsJson must include at least one command."));
            }

            return Fin.Succ<IReadOnlyList<SshAgentCommandRequest>>(commands);
        }
        catch (JsonException exception)
        {
            return Fin.Fail<IReadOnlyList<SshAgentCommandRequest>>(Error.New($"Invalid ssh.commandsJson: {exception.Message}"));
        }
    }

    private static Fin<IReadOnlyList<string>> ParseArgumentsJson(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return Fin.Succ<IReadOnlyList<string>>(Array.Empty<string>());
        }

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Fin.Fail<IReadOnlyList<string>>(Error.New("ssh.argumentsJson must be a JSON array."));
            }

            return Fin.Succ<IReadOnlyList<string>>(ReadStringArray(document.RootElement));
        }
        catch (JsonException exception)
        {
            return Fin.Fail<IReadOnlyList<string>>(Error.New($"Invalid ssh.argumentsJson: {exception.Message}"));
        }
    }

    private static AgentPluginExecutionResult ToPluginResult(SshAgentLaunchResponse response)
    {
        return AgentPluginExecutionResult.Success(
            response.Status,
            response.Summary,
            response.AgentId,
            new Dictionary<string, string?>
            {
                ["plugin"] = PluginName,
                ["ssh.agentId"] = response.AgentId,
                ["ssh.profile"] = response.Profile,
                ["ssh.commandCount"] = response.CommandCount.ToString(CultureInfo.InvariantCulture),
                ["ssh.pollIntervalMilliseconds"] = response.PollIntervalMilliseconds.ToString(CultureInfo.InvariantCulture)
            });
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> parameters, string primaryKey, string fallbackKey)
    {
        if (parameters.TryGetValue(primaryKey, out var primary) && !string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        return parameters.TryGetValue(fallbackKey, out var fallback) && !string.IsNullOrWhiteSpace(fallback)
            ? fallback
            : null;
    }

    private static int? TryGetInt32(IReadOnlyDictionary<string, string?> parameters, string primaryKey, string fallbackKey)
    {
        var value = GetValue(parameters, primaryKey, fallbackKey);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return ReadStringArray(property);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement arrayElement)
    {
        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                values.Add(item.GetString() ?? string.Empty);
            }
        }

        return values;
    }
}
