using System.Diagnostics;
using System.Text.Json;
using LanguageExt;
using MCPServer.Client;
using MCPServer.Client.Stdio;
using MCPServer.Domain.Mcp;
using MCPServer.Host.Sidecar.Profiles;
using MCPServer.Host.Sidecar.Vault;

return await SshHostSidecarConsole.RunAsync(args, CancellationToken.None).ConfigureAwait(false);

internal static class SshHostSidecarConsole
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var options = SidecarOptions.Parse(args);
            if (options.ShowHelp || options.Command is null)
            {
                Console.WriteLine(SidecarOptions.HelpText);
                return 0;
            }

            return options.Command switch
            {
                "vault" => await RunVaultAsync(options, cancellationToken).ConfigureAwait(false),
                "profile" => await RunProfileAsync(options, cancellationToken).ConfigureAwait(false),
                "ssh" => await RunSshAsync(options, cancellationToken).ConfigureAwait(false),
                "run" => await RunMcpServerAsync(options, cancellationToken).ConfigureAwait(false),
                "serve" => await ServeMcpServerAsync(options, cancellationToken).ConfigureAwait(false),
                _ => UnknownCommand(options.Command)
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 130;
        }
    }

    private static async ValueTask<int> RunVaultAsync(SidecarOptions options, CancellationToken cancellationToken)
    {
        var subcommand = options.SubcommandOrThrow("vault");
        var vault = CreateVaultStore(options);

        return subcommand switch
        {
            "list" => await VaultListAsync(vault, options, cancellationToken).ConfigureAwait(false),
            "set" or "add" or "upsert" => await VaultSetAsync(vault, options, cancellationToken).ConfigureAwait(false),
            "delete" or "remove" => await VaultDeleteAsync(vault, options, cancellationToken).ConfigureAwait(false),
            "verify" => await VaultVerifyAsync(vault, options, cancellationToken).ConfigureAwait(false),
            _ => UnknownCommand($"vault {subcommand}")
        };
    }

    private static async ValueTask<int> VaultListAsync(
        SshCredentialVaultStore vault,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var entries = await vault.ListEntriesAsync(cancellationToken).ConfigureAwait(false);
        if (options.JsonOutput)
        {
            WriteVaultListJson(entries);
            return 0;
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("No SSH vault entries found.");
            return 0;
        }

        foreach (var entry in entries)
        {
            var description = string.IsNullOrWhiteSpace(entry.Description) ? string.Empty : $" - {entry.Description}";
            Console.WriteLine($"{entry.Name} => {SshVaultEnvironment.BuildVariableName(entry.Name)}{description}");
        }

        return 0;
    }

    private static async ValueTask<int> VaultSetAsync(
        SshCredentialVaultStore vault,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var name = options.PositionalOrRequired("name", 0);
        var secret = options.Value("secret")
            ?? (options.Value("secret-file") is { } secretFile ? await File.ReadAllTextAsync(secretFile, cancellationToken).ConfigureAwait(false) : null)
            ?? ReadSecretFromConsole($"SSH secret for '{name}': ");

        var entry = await vault.UpsertEntryAsync(name, secret, options.Value("description"), cancellationToken).ConfigureAwait(false);
        if (options.JsonOutput)
        {
            WriteVaultSetJson(entry);
            return 0;
        }

        Console.WriteLine($"Saved SSH vault item '{entry.Name}'. Use environment variable {SshVaultEnvironment.BuildVariableName(entry.Name)} from profiles.");
        return 0;
    }

    private static async ValueTask<int> VaultDeleteAsync(
        SshCredentialVaultStore vault,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var name = options.PositionalOrRequired("name", 0);
        var deleted = await vault.DeleteEntryAsync(name, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            Console.Error.WriteLine($"No SSH vault item named '{name}' was found.");
            return 1;
        }

        Console.WriteLine($"Deleted SSH vault item '{name}'.");
        return 0;
    }

    private static async ValueTask<int> VaultVerifyAsync(
        SshCredentialVaultStore vault,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var name = options.PositionalOrRequired("name", 0);
        var expected = options.Value("expected")
            ?? (options.Value("expected-file") is { } expectedFile ? await File.ReadAllTextAsync(expectedFile, cancellationToken).ConfigureAwait(false) : null)
            ?? ReadSecretFromConsole($"Expected SSH secret for '{name}': ");
        var actual = await vault.ResolveSecretAsync(name, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"SSH vault item '{name}' did not match the expected secret.");
            return 1;
        }

        Console.WriteLine($"Verified SSH vault item '{name}'.");
        return 0;
    }

    private static async ValueTask<int> RunProfileAsync(SidecarOptions options, CancellationToken cancellationToken)
    {
        var subcommand = options.SubcommandOrThrow("profile");
        var store = CreateProfileStore(options);

        return subcommand switch
        {
            "list" => await ProfileListAsync(store, options, cancellationToken).ConfigureAwait(false),
            "upsert" or "add" or "update" => await ProfileUpsertAsync(store, options, cancellationToken).ConfigureAwait(false),
            "replace" or "overwrite" => await ProfileReplaceAsync(store, options, cancellationToken).ConfigureAwait(false),
            "delete" or "remove" => await ProfileDeleteAsync(store, options, cancellationToken).ConfigureAwait(false),
            "link-password" => await ProfileLinkPasswordAsync(store, options, cancellationToken).ConfigureAwait(false),
            "link-key-passphrase" => await ProfileLinkKeyPassphraseAsync(store, options, cancellationToken).ConfigureAwait(false),
            _ => UnknownCommand($"profile {subcommand}")
        };
    }

    private static async ValueTask<int> RunSshAsync(SidecarOptions options, CancellationToken cancellationToken)
    {
        var subcommand = options.SubcommandOrThrow("ssh");
        var profileStore = CreateProfileStore(options);
        var vault = CreateVaultStore(options);

        return subcommand switch
        {
            "list" or "profiles" => await ProfileListAsync(profileStore, options, cancellationToken).ConfigureAwait(false),
            "delete" or "remove" => await ProfileDeleteAsync(profileStore, options, cancellationToken).ConfigureAwait(false),
            "add-password" or "password" => await SshAddPasswordProfileAsync(profileStore, vault, options, cancellationToken).ConfigureAwait(false),
            "add-key" or "key" => await SshAddKeyProfileAsync(profileStore, vault, options, cancellationToken).ConfigureAwait(false),
            _ => UnknownCommand($"ssh {subcommand}")
        };
    }

    private static async ValueTask<int> SshAddPasswordProfileAsync(
        SshProfileSidecarStore profileStore,
        SshCredentialVaultStore vault,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var profileName = options.PositionalOrRequired("profile", 0);
        var host = RequiredOption(options, "host");
        var username = RequiredOption(options, "username");
        var replaceProfile = ShouldReplaceProfile(options);
        var allowedRoot = options.BoolValue("allowed-root");
        var rootRiskAllowedRoot = replaceProfile ? allowedRoot ?? false : allowedRoot;

        if (!await ValidateRootRiskAsync(profileStore, options, profileName, username, rootRiskAllowedRoot, cancellationToken).ConfigureAwait(false))
        {
            return 2;
        }

        var vaultItem = options.Value("vault-item") ?? $"{profileName}-password";
        var password = await ReadSecretOptionAsync(
            options,
            "password",
            "password-file",
            $"SSH password for profile '{profileName}': ",
            cancellationToken).ConfigureAwait(false);

        await vault.UpsertEntryAsync(
                vaultItem,
                password,
                options.Value("description") ?? $"Password for SSH profile '{profileName}'",
                cancellationToken)
            .ConfigureAwait(false);

        var request = CreateProfileRequestFromOptions(
            options,
            host,
            username,
            passwordVaultItem: vaultItem,
            privateKeyPath: null,
            privateKeyPassphraseVaultItem: null);

        var profile = replaceProfile
            ? await profileStore.ReplaceProfileAsync(profileName, request, cancellationToken).ConfigureAwait(false)
            : await profileStore.UpsertProfileAsync(profileName, request, cancellationToken).ConfigureAwait(false);
        if (options.JsonOutput)
        {
            WriteProfileUpsertJson(profileStore.Path, profileName, profile);
            return 0;
        }

        Console.WriteLine($"{(replaceProfile ? "Replaced" : "Saved")} SSH password profile '{profileName}'.");
        Console.WriteLine($"  profile file : {profileStore.Path}");
        Console.WriteLine($"  vault item   : {vaultItem} ({SshVaultEnvironment.BuildVariableName(vaultItem)})");
        return 0;
    }

    private static async ValueTask<int> SshAddKeyProfileAsync(
        SshProfileSidecarStore profileStore,
        SshCredentialVaultStore vault,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var profileName = options.PositionalOrRequired("profile", 0);
        var host = RequiredOption(options, "host");
        var username = RequiredOption(options, "username");
        var privateKeyPath = RequiredOption(options, "private-key-path");
        var replaceProfile = ShouldReplaceProfile(options);
        var allowedRoot = options.BoolValue("allowed-root");
        var rootRiskAllowedRoot = replaceProfile ? allowedRoot ?? false : allowedRoot;

        if (!await ValidateRootRiskAsync(profileStore, options, profileName, username, rootRiskAllowedRoot, cancellationToken).ConfigureAwait(false))
        {
            return 2;
        }

        string? passphraseVaultItem = null;
        if (options.HasValue("key-passphrase") || options.HasValue("key-passphrase-file") || options.BoolValue("prompt-key-passphrase") == true)
        {
            passphraseVaultItem = options.Value("vault-item") ?? $"{profileName}-key-passphrase";
            var passphrase = await ReadSecretOptionAsync(
                options,
                "key-passphrase",
                "key-passphrase-file",
                $"SSH private key passphrase for profile '{profileName}': ",
                cancellationToken).ConfigureAwait(false);

            await vault.UpsertEntryAsync(
                    passphraseVaultItem,
                    passphrase,
                    options.Value("description") ?? $"Private key passphrase for SSH profile '{profileName}'",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var request = CreateProfileRequestFromOptions(
            options,
            host,
            username,
            passwordVaultItem: null,
            privateKeyPath: privateKeyPath,
            privateKeyPassphraseVaultItem: passphraseVaultItem);

        var profile = replaceProfile
            ? await profileStore.ReplaceProfileAsync(profileName, request, cancellationToken).ConfigureAwait(false)
            : await profileStore.UpsertProfileAsync(profileName, request, cancellationToken).ConfigureAwait(false);
        if (options.JsonOutput)
        {
            WriteProfileUpsertJson(profileStore.Path, profileName, profile);
            return 0;
        }

        Console.WriteLine($"{(replaceProfile ? "Replaced" : "Saved")} SSH key profile '{profileName}'.");
        Console.WriteLine($"  profile file : {profileStore.Path}");
        Console.WriteLine($"  key path     : {privateKeyPath}");
        if (!string.IsNullOrWhiteSpace(passphraseVaultItem))
        {
            Console.WriteLine($"  passphrase   : {passphraseVaultItem} ({SshVaultEnvironment.BuildVariableName(passphraseVaultItem)})");
        }

        return 0;
    }

    private static async ValueTask<int> ProfileListAsync(
        SshProfileSidecarStore store,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var profiles = await store.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
        if (options.JsonOutput)
        {
            WriteProfileListJson(store.Path, profiles);
            return 0;
        }

        if (profiles.Count == 0)
        {
            Console.WriteLine($"No SSH profiles found in {store.Path}.");
            return 0;
        }

        foreach (var profile in profiles.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var rootMarker = profile.Value.AllowedRoot ? " [allowedRoot]" : string.Empty;
            Console.WriteLine($"{profile.Key}: {profile.Value.Username}@{profile.Value.Host}:{profile.Value.Port}{rootMarker}");
        }

        return 0;
    }

    private static async ValueTask<int> ProfileUpsertAsync(
        SshProfileSidecarStore store,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var name = options.PositionalOrRequired("name", 0);
        var request = CreateProfileRequestFromOptions(
            options,
            host: null,
            username: null,
            passwordVaultItem: null,
            privateKeyPath: null,
            privateKeyPassphraseVaultItem: null);

        if (!await ValidateRootRiskAsync(store, options, name, request.Username, request.AllowedRoot, cancellationToken).ConfigureAwait(false))
        {
            return 2;
        }

        var profile = await store.UpsertProfileAsync(name, request, cancellationToken).ConfigureAwait(false);
        if (options.JsonOutput)
        {
            WriteProfileUpsertJson(store.Path, name, profile);
            return 0;
        }

        Console.WriteLine($"Saved SSH profile '{name}' to {store.Path}.");
        return 0;
    }

    private static async ValueTask<int> ProfileReplaceAsync(
        SshProfileSidecarStore store,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var name = options.PositionalOrRequired("name", 0);
        var request = CreateProfileRequestFromOptions(
            options,
            host: RequiredOption(options, "host"),
            username: RequiredOption(options, "username"),
            passwordVaultItem: null,
            privateKeyPath: null,
            privateKeyPassphraseVaultItem: null);

        var rootRiskAllowedRoot = request.AllowedRoot ?? false;
        if (!await ValidateRootRiskAsync(store, options, name, request.Username, rootRiskAllowedRoot, cancellationToken).ConfigureAwait(false))
        {
            return 2;
        }

        var profile = await store.ReplaceProfileAsync(name, request, cancellationToken).ConfigureAwait(false);
        if (options.JsonOutput)
        {
            WriteProfileUpsertJson(store.Path, name, profile);
            return 0;
        }

        Console.WriteLine($"Replaced SSH profile '{name}' in {store.Path}.");
        return 0;
    }

    private static async ValueTask<int> ProfileDeleteAsync(
        SshProfileSidecarStore store,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var name = options.PositionalOrRequired("name", 0);
        var deleted = await store.DeleteProfileAsync(name, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            Console.Error.WriteLine($"No SSH profile named '{name}' was found.");
            return 1;
        }

        Console.WriteLine($"Deleted SSH profile '{name}'.");
        return 0;
    }

    private static async ValueTask<int> ProfileLinkPasswordAsync(
        SshProfileSidecarStore store,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var profile = options.PositionalOrRequired("profile", 0);
        var item = options.PositionalOrRequired("vault-item", 1);
        await store.LinkPasswordAsync(profile, item, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Linked profile '{profile}' password to vault item '{item}' ({SshVaultEnvironment.BuildVariableName(item)}).");
        return 0;
    }

    private static async ValueTask<int> ProfileLinkKeyPassphraseAsync(
        SshProfileSidecarStore store,
        SidecarOptions options,
        CancellationToken cancellationToken)
    {
        var profile = options.PositionalOrRequired("profile", 0);
        var item = options.PositionalOrRequired("vault-item", 1);
        await store.LinkPrivateKeyPassphraseAsync(profile, item, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Linked profile '{profile}' private-key passphrase to vault item '{item}' ({SshVaultEnvironment.BuildVariableName(item)}).");
        return 0;
    }

    private static bool ShouldReplaceProfile(SidecarOptions options) =>
        options.BoolValue("replace") == true ||
        options.BoolValue("overwrite") == true;

    private static ProfileUpsertRequest CreateProfileRequestFromOptions(
        SidecarOptions options,
        string? host,
        string? username,
        string? passwordVaultItem,
        string? privateKeyPath,
        string? privateKeyPassphraseVaultItem)
    {
        return new ProfileUpsertRequest
        {
            Host = host ?? options.Value("host"),
            Port = options.IntValue("port"),
            Username = username ?? options.Value("username"),
            PrivateKeyPath = privateKeyPath ?? options.Value("private-key-path"),
            PasswordVaultItem = passwordVaultItem ?? options.Value("password-vault-item"),
            PrivateKeyPassphraseVaultItem = privateKeyPassphraseVaultItem ?? options.Value("key-passphrase-vault-item"),
            HostKeySha256 = options.Value("host-key-sha256"),
            AcceptUnknownHostKey = options.BoolValue("accept-unknown-host-key"),
            WorkingDirectory = options.Value("working-directory"),
            AllowedCommands = options.Values("allowed-command"),
            DeniedCommands = options.Values("denied-command"),
            AllowedRemotePathPrefixes = options.Values("allowed-prefix"),
            AllowSudoCommand = options.BoolValue("allow-sudo"),
            AllowAllCommands = options.BoolValue("allow-all-commands"),
            Privileged = options.BoolValue("privileged"),
            AllowedRoot = options.BoolValue("allowed-root")
        };
    }

    private static async ValueTask<bool> ValidateRootRiskAsync(
        SshProfileSidecarStore store,
        SidecarOptions options,
        string profileName,
        string? requestedUsername,
        bool? requestedAllowedRoot,
        CancellationToken cancellationToken)
    {
        var existingProfiles = await store.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
        existingProfiles.TryGetValue(profileName, out var existingProfile);

        var effectiveUsername = string.IsNullOrWhiteSpace(requestedUsername) ? existingProfile?.Username : requestedUsername;
        var effectiveAllowedRoot = requestedAllowedRoot ?? existingProfile?.AllowedRoot ?? false;
        var usesRootUsername = string.Equals(effectiveUsername, "root", StringComparison.OrdinalIgnoreCase);

        if (requestedAllowedRoot == false)
        {
            return true;
        }

        if (!usesRootUsername && requestedAllowedRoot != true && !effectiveAllowedRoot)
        {
            return true;
        }

        WriteAllowedRootWarning(profileName, effectiveUsername, effectiveAllowedRoot);

        if (usesRootUsername && !effectiveAllowedRoot)
        {
            if (string.Equals(requestedUsername, "root", StringComparison.OrdinalIgnoreCase) || existingProfile is null)
            {
                Console.Error.WriteLine("Refusing to create or update a root SSH profile without --allowed-root true.");
                return false;
            }

            return true;
        }

        if (options.BoolValue("i-understand-root-ai-risk") != true)
        {
            Console.Error.WriteLine("Refusing root-capable SSH profile update. Re-run with --i-understand-root-ai-risk true to acknowledge the risk.");
            return false;
        }

        return true;
    }

    private static void WriteAllowedRootWarning(string profileName, string? username, bool allowedRoot)
    {
        const string reset = "\u001b[0m";
        const string danger = "\u001b[30;43m";
        var useAnsi = !Console.IsErrorRedirected;
        var prefix = useAnsi ? danger : string.Empty;
        var suffix = useAnsi ? reset : string.Empty;
        var rootState = allowedRoot ? "allowedRoot=true" : "allowedRoot=false";

        Console.Error.WriteLine(prefix + "╔══════════════════════════════════════════════════════════════════════════════╗" + suffix);
        Console.Error.WriteLine(prefix + "║                           SSH ROOT + AI DANGER                              ║" + suffix);
        Console.Error.WriteLine(prefix + "╠══════════════════════════════════════════════════════════════════════════════╣" + suffix);
        Console.Error.WriteLine(prefix + $"║ Profile: {TruncateForBox(profileName, 63),-63} ║" + suffix);
        Console.Error.WriteLine(prefix + $"║ Username: {TruncateForBox(username ?? "<unchanged>", 62),-62} ║" + suffix);
        Console.Error.WriteLine(prefix + $"║ Root switch: {TruncateForBox(rootState, 59),-59} ║" + suffix);
        Console.Error.WriteLine(prefix + "║                                                                              ║" + suffix);
        Console.Error.WriteLine(prefix + "║ SSH as root lets an AI-driven tool change ownership, delete data, install     ║" + suffix);
        Console.Error.WriteLine(prefix + "║ packages, rewrite services, rotate keys, and take down hosts. You can         ║" + suffix);
        Console.Error.WriteLine(prefix + "║ seriously fuck some shit up with SSH root and AI.                             ║" + suffix);
        Console.Error.WriteLine(prefix + "║                                                                              ║" + suffix);
        Console.Error.WriteLine(prefix + "║ Use only isolated targets, pinned host keys, audited traces, explicit command  ║" + suffix);
        Console.Error.WriteLine(prefix + "║ allowlists, and a disposable recovery path.                                   ║" + suffix);
        Console.Error.WriteLine(prefix + "╚══════════════════════════════════════════════════════════════════════════════╝" + suffix);
    }

    private static string TruncateForBox(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static async ValueTask<string> ReadSecretOptionAsync(
        SidecarOptions options,
        string secretOptionName,
        string fileOptionName,
        string prompt,
        CancellationToken cancellationToken)
    {
        if (options.Value(secretOptionName) is { } secret)
        {
            return secret;
        }

        if (options.Value(fileOptionName) is { } secretFile)
        {
            return await File.ReadAllTextAsync(secretFile, cancellationToken).ConfigureAwait(false);
        }

        return ReadSecretFromConsole(prompt);
    }

    private static string RequiredOption(SidecarOptions options, string key)
    {
        return options.Value(key) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"--{key} is required.");
    }

    private static async ValueTask<int> ServeMcpServerAsync(SidecarOptions options, CancellationToken cancellationToken)
    {
        var serverPath = options.Value("server-path");
        if (string.IsNullOrWhiteSpace(serverPath))
        {
            throw new ArgumentException("serve requires --server-path <path-to-MCPServer.Host.exe>.");
        }

        var profileStore = CreateProfileStore(options);
        var vault = CreateVaultStore(options);
        var referencedVariables = await profileStore.GetReferencedEnvironmentVariablesAsync(cancellationToken).ConfigureAwait(false);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["McpTools__Ssh__Enabled"] = "true",
            ["McpTools__Ssh__ProfilePath"] = profileStore.Path
        };

        var vaultEnvironment = await vault.ExportReferencedEnvironmentAsync(referencedVariables, cancellationToken).ConfigureAwait(false);
        foreach (var variable in vaultEnvironment)
        {
            environment[variable.Key] = variable.Value;
        }

        using var process = StartServerProcess(serverPath, options.Value("server-working-directory"), environment);

        var stdinToServer = CopyClientInputToServerAsync(process, cancellationToken);
        var serverToStdout = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput(), cancellationToken);
        var serverErrToStderr = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError(), cancellationToken);
        var exited = process.WaitForExitAsync(cancellationToken);

        var completed = await Task.WhenAny(exited, serverToStdout, serverErrToStderr).ConfigureAwait(false);
        if (completed != exited && process.HasExited)
        {
            await exited.ConfigureAwait(false);
        }
        else if (completed != exited && !process.HasExited && (serverToStdout.IsFaulted || serverErrToStderr.IsFaulted))
        {
            TryKill(process);
        }

        try
        {
            await Task.WhenAll(serverToStdout, serverErrToStderr).ConfigureAwait(false);
        }
        catch (IOException)
        {
            if (!process.HasExited)
            {
                TryKill(process);
            }
        }

        if (!stdinToServer.IsCompleted)
        {
            TryKill(process);
        }

        try
        {
            await stdinToServer.ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The child process closed stdin. That is a normal shutdown path for stdio servers.
        }
        catch (ObjectDisposedException)
        {
            // The child process closed stdin during shutdown.
        }

        if (!process.HasExited)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        return process.ExitCode;
    }

    private static Process StartServerProcess(string serverPath, string? workingDirectory, IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = serverPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory.Trim();
        }
        else
        {
            var serverDirectory = Path.GetDirectoryName(Path.GetFullPath(serverPath));
            if (!string.IsNullOrWhiteSpace(serverDirectory))
            {
                startInfo.WorkingDirectory = serverDirectory;
            }
        }

        foreach (var variable in environment)
        {
            if (variable.Value is not null)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start MCP server process '{serverPath}'.");
        }

        return process;
    }

    private static async Task CopyClientInputToServerAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await Console.OpenStandardInput().CopyToAsync(process.StandardInput.BaseStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async ValueTask<int> RunMcpServerAsync(SidecarOptions options, CancellationToken cancellationToken)
    {
        var serverPath = options.Value("server-path");
        if (string.IsNullOrWhiteSpace(serverPath))
        {
            throw new ArgumentException("run requires --server-path <path-to-MCPServer.Host.exe>.");
        }

        var profileStore = CreateProfileStore(options);
        var vault = CreateVaultStore(options);
        var referencedVariables = await profileStore.GetReferencedEnvironmentVariablesAsync(cancellationToken).ConfigureAwait(false);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["McpTools__Ssh__Enabled"] = "true",
            ["McpTools__Ssh__ProfilePath"] = profileStore.Path
        };

        var vaultEnvironment = await vault.ExportReferencedEnvironmentAsync(referencedVariables, cancellationToken).ConfigureAwait(false);
        foreach (var variable in vaultEnvironment)
        {
            environment[variable.Key] = variable.Value;
        }

        var processOptions = new McpClientProcessOptions
        {
            ServerExecutablePath = serverPath,
            WorkingDirectory = options.Value("server-working-directory"),
            ClientName = "mcpserver-host-sidecar",
            ClientTitle = "MCP Server Host Sidecar",
            ClientVersion = "1.0.0",
            EnvironmentVariables = environment
        };

        var started = await StdioMcpClientSession.StartAsync(processOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (started.IsFail)
        {
            Console.Error.WriteLine(GetError(started));
            return 1;
        }

        await using var session = GetValue(started);
        var initialized = await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (initialized.IsFail)
        {
            Console.Error.WriteLine(GetError(initialized));
            return 1;
        }

        var initializeResult = GetValue(initialized);
        Console.WriteLine($"Connected to {initializeResult.ServerInfo.Name} {initializeResult.ServerInfo.Version} using MCP {initializeResult.ProtocolVersion}.");
        Console.WriteLine($"SSH profile file: {profileStore.Path}");
        Console.WriteLine($"Hydrated SSH vault environment variables: {vaultEnvironment.Count}");

        var toolName = options.Value("tool");
        if (string.IsNullOrWhiteSpace(toolName))
        {
            var tools = await session.ListToolsAsync(cursor: null, cancellationToken).ConfigureAwait(false);
            if (tools.IsFail)
            {
                Console.Error.WriteLine(GetError(tools));
                return 1;
            }

            foreach (var tool in GetValue(tools).Tools)
            {
                Console.WriteLine($"- {tool.Name}: {tool.Description}");
            }

            return 0;
        }

        JsonElement? toolArguments = null;
        using var argumentsDocument = ParseArguments(options.Value("arguments"), out toolArguments, out var argumentError);
        if (!string.IsNullOrWhiteSpace(argumentError))
        {
            Console.Error.WriteLine(argumentError);
            return 2;
        }

        var call = await session.CallToolAsync(toolName, toolArguments, cancellationToken).ConfigureAwait(false);
        if (call.IsFail)
        {
            Console.Error.WriteLine(GetError(call));
            return 1;
        }

        PrintToolResult(GetValue(call));
        return 0;
    }

    private static SshCredentialVaultStore CreateVaultStore(SidecarOptions options)
    {
        var baseDirectory = options.Value("base-directory");
        return new SshCredentialVaultStore(
            options.Value("vault-path") ?? DefaultVaultPath(),
            options.Value("vault-key-path") ?? DefaultVaultKeyPath(),
            baseDirectory);
    }

    private static SshProfileSidecarStore CreateProfileStore(SidecarOptions options)
    {
        var baseDirectory = options.Value("base-directory");
        return new SshProfileSidecarStore(options.Value("profile-path") ?? DefaultProfilePath(), baseDirectory);
    }

    private static string DefaultBasePath() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McpServer", "ssh");

    private static string DefaultVaultPath() => System.IO.Path.Combine(DefaultBasePath(), "ssh-vault.local.json");

    private static string DefaultVaultKeyPath() => System.IO.Path.Combine(DefaultBasePath(), "ssh-vault.key");

    private static string DefaultProfilePath() => System.IO.Path.Combine(DefaultBasePath(), "ssh-profiles.local.json");

    private static JsonDocument? ParseArguments(string? json, out JsonElement? arguments, out string? error)
    {
        arguments = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                error = "--arguments must be a JSON object.";
                return null;
            }

            arguments = document.RootElement.Clone();
            return document;
        }
        catch (JsonException ex)
        {
            error = $"--arguments is not valid JSON: {ex.Message}";
            return null;
        }
    }

    private static void PrintToolResult(ToolCallResult result)
    {
        Console.WriteLine(result.IsError ? "Tool returned an error result." : "Tool returned a success result.");
        foreach (var content in result.Content)
        {
            if (content is TextToolContent text)
            {
                Console.WriteLine(text.Text);
            }
        }

        if (result.StructuredContent is { } structuredContent)
        {
            Console.WriteLine();
            Console.WriteLine("Structured content:");
            Console.WriteLine(JsonSerializer.Serialize(structuredContent, McpJsonSerializerContext.Default.JsonElement));
        }
    }

    private static T GetValue<T>(Fin<T> value)
    {
        return value.Match(
            Succ: static result => result,
            Fail: static error => throw new InvalidOperationException(error.Message));
    }

    private static string GetError<T>(Fin<T> value)
    {
        return value.Match(
            Succ: static _ => string.Empty,
            Fail: static error => error.Message);
    }

    private static string ReadSecretFromConsole(string prompt)
    {
        Console.Error.Write(prompt);
        var builder = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }
    }

    private static void WriteVaultListJson(IReadOnlyList<SshCredentialVaultEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = CreateJsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("command", "vault list");
            writer.WriteStartArray("entries");
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WriteString("environmentVariable", SshVaultEnvironment.BuildVariableName(entry.Name));
                if (!string.IsNullOrWhiteSpace(entry.Description))
                {
                    writer.WriteString("description", entry.Description);
                }

                writer.WriteString("createdUtc", entry.CreatedUtc);
                writer.WriteString("updatedUtc", entry.UpdatedUtc);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void WriteVaultSetJson(SshCredentialVaultEntry entry)
    {
        using var stream = new MemoryStream();
        using (var writer = CreateJsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("command", "vault set");
            writer.WriteString("name", entry.Name);
            writer.WriteString("environmentVariable", SshVaultEnvironment.BuildVariableName(entry.Name));
            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                writer.WriteString("description", entry.Description);
            }

            writer.WriteString("status", "saved");
            writer.WriteEndObject();
        }

        Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void WriteProfileListJson(string path, IReadOnlyDictionary<string, SidecarSshProfile> profiles)
    {
        using var stream = new MemoryStream();
        using (var writer = CreateJsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("command", "profile list");
            writer.WriteString("path", path);
            writer.WriteStartObject("profiles");
            foreach (var item in profiles.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(item.Key);
                WriteProfile(writer, item.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void WriteProfileUpsertJson(string path, string name, SidecarSshProfile profile)
    {
        using var stream = new MemoryStream();
        using (var writer = CreateJsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("command", "profile upsert");
            writer.WriteString("name", name);
            writer.WriteString("path", path);
            writer.WritePropertyName("profile");
            WriteProfile(writer, profile);
            writer.WriteEndObject();
        }

        Console.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static Utf8JsonWriter CreateJsonWriter(Stream stream) =>
        new(stream, new JsonWriterOptions { Indented = true });

    private static void WriteProfile(Utf8JsonWriter writer, SidecarSshProfile profile)
    {
        writer.WriteStartObject();
        writer.WriteString("host", profile.Host);
        writer.WriteNumber("port", profile.Port);
        writer.WriteString("username", profile.Username);
        WriteOptionalString(writer, "privateKeyPath", profile.PrivateKeyPath);
        WriteOptionalString(writer, "privateKeyPassphraseEnvironmentVariable", profile.PrivateKeyPassphraseEnvironmentVariable);
        WriteOptionalString(writer, "passwordEnvironmentVariable", profile.PasswordEnvironmentVariable);
        WriteOptionalString(writer, "hostKeySha256", profile.HostKeySha256);
        writer.WriteBoolean("acceptUnknownHostKey", profile.AcceptUnknownHostKey);
        WriteOptionalString(writer, "workingDirectory", profile.WorkingDirectory);
        WriteStringArray(writer, "allowedCommands", profile.AllowedCommands);
        WriteStringArray(writer, "deniedCommands", profile.DeniedCommands);
        WriteStringArray(writer, "allowedRemotePathPrefixes", profile.AllowedRemotePathPrefixes);
        writer.WriteBoolean("allowSudoCommand", profile.AllowSudoCommand);
        writer.WriteBoolean("allowAllCommands", profile.AllowAllCommands);
        writer.WriteBoolean("privileged", profile.Privileged);
        writer.WriteBoolean("allowedRoot", profile.AllowedRoot);
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string propertyName, IEnumerable<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        return 2;
    }
}

internal sealed class SidecarOptions
{
    private readonly Dictionary<string, List<string>> _values;

    private SidecarOptions(string? command, string? subcommand, IReadOnlyList<string> positionals, Dictionary<string, List<string>> values, bool showHelp)
    {
        Command = command;
        Subcommand = subcommand;
        Positionals = positionals;
        _values = values;
        ShowHelp = showHelp;
    }

    public string? Command { get; }

    public string? Subcommand { get; }

    public IReadOnlyList<string> Positionals { get; }

    public bool ShowHelp { get; }

    public bool JsonOutput => string.Equals(Value("output"), "json", StringComparison.OrdinalIgnoreCase);

    public static string HelpText => """
    Usage:
      MCPServer.Host.Sidecar vault list [--output json]
      MCPServer.Host.Sidecar vault set <name> --secret <secret> [--description <text>]
      MCPServer.Host.Sidecar vault delete <name>
      MCPServer.Host.Sidecar vault verify <name> --expected <secret>

      MCPServer.Host.Sidecar profile list [--output json]
      MCPServer.Host.Sidecar profile upsert <name> --host <host> --username <user> [options]
      MCPServer.Host.Sidecar profile replace <name> --host <host> --username <user> [options]
      MCPServer.Host.Sidecar profile link-password <profile> <vault-item>
      MCPServer.Host.Sidecar profile link-key-passphrase <profile> <vault-item>
      MCPServer.Host.Sidecar profile delete <name>

      MCPServer.Host.Sidecar ssh add-password <profile> --host <host> --username <user> [--password <secret>] [options]
      MCPServer.Host.Sidecar ssh add-key <profile> --host <host> --username <user> --private-key-path <path> [options]
      MCPServer.Host.Sidecar ssh list [--output json]
      MCPServer.Host.Sidecar ssh delete <profile>

      MCPServer.Host.Sidecar run --server-path <path-to-MCPServer.Host.exe> [--tool <name>] [--arguments <json>]
      MCPServer.Host.Sidecar serve --server-path <path-to-MCPServer.Host.exe>

    Shared options:
      --base-directory <dir>       Base directory for relative paths.
      --vault-path <path>          Vault file path. Defaults to user LocalAppData/McpServer/ssh/ssh-vault.local.json.
      --vault-key-path <path>      Vault key file path. Defaults to user LocalAppData/McpServer/ssh/ssh-vault.key.
      --profile-path <path>        SSH profile path. Defaults to user LocalAppData/McpServer/ssh/ssh-profiles.local.json.
      --output json                Emit JSON for list/upsert/replace commands.

    Profile upsert options:
      --port <port>
      --private-key-path <path>
      --password-vault-item <name>
      --key-passphrase-vault-item <name>
      --host-key-sha256 <SHA256:fingerprint>
      --accept-unknown-host-key <true|false>
      --working-directory <path>
      --allowed-command <name>     Repeatable.
      --denied-command <name>      Repeatable.
      --allowed-prefix <path>      Repeatable.
      --allow-sudo <true|false>
      --allow-all-commands <true|false>
      --privileged <true|false>
      --allowed-root <true|false>
      --i-understand-root-ai-risk <true|false>
      --replace <true|false>       For ssh add-password/add-key, replace the whole existing profile instead of merging.
      --overwrite <true|false>     Alias for --replace.

    Convenience SSH options:
      --password <secret>                 Used by ssh add-password. Prompts when omitted.
      --password-file <path>              Read password from file.
      --private-key-path <path>           Used by ssh add-key.
      --key-passphrase <secret>           Optional private-key passphrase.
      --key-passphrase-file <path>        Read private-key passphrase from file.
      --prompt-key-passphrase <true|false>
      --vault-item <name>                 Override default generated vault item name.
    """;

    public static SidecarOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new SidecarOptions(null, null, [], new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase), showHelp: true);
        }

        var showHelp = false;
        string? command = null;
        string? subcommand = null;
        var positionals = new List<string>();
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--help" or "-h" or "/?")
            {
                showHelp = true;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..];
                var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++index]
                    : "true";
                AddValue(values, key, value);
                continue;
            }

            if (command is null)
            {
                command = arg.Trim().ToLowerInvariant();
                continue;
            }

            if (subcommand is null && (command is "vault" or "profile" or "ssh"))
            {
                subcommand = arg.Trim().ToLowerInvariant();
                continue;
            }

            positionals.Add(arg);
        }

        return new SidecarOptions(command, subcommand, positionals, values, showHelp);
    }

    public string SubcommandOrThrow(string commandName) =>
        string.IsNullOrWhiteSpace(Subcommand)
            ? throw new ArgumentException($"{commandName} requires a subcommand.")
            : Subcommand;

    public string? Value(string key) =>
        _values.TryGetValue(key, out var values) && values.Count > 0 ? values[^1] : null;

    public bool HasValue(string key) =>
        _values.TryGetValue(key, out var values) && values.Count > 0;

    public IReadOnlyList<string> Values(string key) =>
        _values.TryGetValue(key, out var values) ? values.ToArray() : Array.Empty<string>();

    public string PositionalOrRequired(string name, int index) =>
        Positionals.Count > index && !string.IsNullOrWhiteSpace(Positionals[index])
            ? Positionals[index]
            : throw new ArgumentException($"Required argument missing: {name}.");

    public int? IntValue(string key)
    {
        var raw = Value(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, out var value) ? value : throw new ArgumentException($"--{key} must be an integer.");
    }

    public bool? BoolValue(string key)
    {
        var raw = Value(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return bool.TryParse(raw, out var value) ? value : throw new ArgumentException($"--{key} must be true or false.");
    }

    private static void AddValue(Dictionary<string, List<string>> values, string key, string value)
    {
        if (!values.TryGetValue(key, out var list))
        {
            list = [];
            values[key] = list;
        }

        list.Add(value);
    }
}
