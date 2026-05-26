using Autofac;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Infrastructure;
using MCPServer.Ssh.Interfaces;

namespace MCPServer.Host.Sidecar.Composition;

internal static class SshHostSidecarRuntimeFactory
{
    public static SidecarSshRuntime CreateRuntime(SidecarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = CreateSshSettings(options);
        var builder = new ContainerBuilder();
        builder.RegisterModule(new SshHostSidecarRuntimeModule(settings));

        var scope = builder.Build();
        var pathResolver = scope.Resolve<ISshPathResolver>();
        return new SidecarSshRuntime(
            scope,
            settings,
            ResolveProfileStoreDisplayPath(pathResolver, settings),
            scope.Resolve<ISshProfileManagementStore>(),
            scope.Resolve<ISshCredentialVault>());
    }

    public static SshToolSettings CreateSshSettings(SidecarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return SshToolSettings.Normalize(new SshToolSettings
        {
            Enabled = true,
            ProfileDatabasePath = options.Value("profile-db-path") ?? options.Value("profile-database-path") ?? DefaultProfileDatabasePath(options),
            VaultPath = options.Value("vault-path") ?? DefaultVaultPath(options),
            VaultKeyPath = options.Value("vault-key-path") ?? DefaultVaultKeyPath(options),
            UseLocalCredentialVault = true
        });
    }

    public static string ResolveProfileStoreDisplayPath(SshToolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ResolveProfileStoreDisplayPath(new DefaultSshPathResolver(), settings);
    }

    public static string ResolveProfileStoreDisplayPath(ISshPathResolver pathResolver, SshToolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(settings);

        return settings.ProfileDatabasePath is { Length: > 0 } configured
            ? pathResolver.ResolveConfiguredPath(configured)
            : pathResolver.ResolveUserDataPath(Path.Combine("ssh", "ssh-store.db"));
    }

    private static string DefaultBasePath(SidecarOptions? options = null)
    {
        var configured = options?.Value("base-directory");
        return !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured.Trim())
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McpServer", "ssh");
    }

    private static string DefaultVaultPath(SidecarOptions? options = null) => Path.Combine(DefaultBasePath(options), "ssh-vault.local.json");

    private static string DefaultVaultKeyPath(SidecarOptions? options = null) => Path.Combine(DefaultBasePath(options), "ssh-vault.key");

    private static string DefaultProfileDatabasePath(SidecarOptions? options = null) => Path.Combine(DefaultBasePath(options), "ssh-store.db");
}
