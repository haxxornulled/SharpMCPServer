using MCPServer.ExecutionPlugins.Ssh.Tests.Testing;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Infrastructure;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MCPServer.AgentRouter.IntegrationTests.Ssh.Profiles;

public sealed class SqliteSshProfileStoreTests
{
    [Fact]
    public async Task LoadProfilesAsync_Imports_Dictionary_Shaped_Json_When_Database_Is_Empty()
    {
        await using var temp = TempSshProfileStore.Create();
        await temp.WriteProfilesJsonAsync("""
        {
          "profiles": {
            "debian-root-lab": {
              "displayName": "Debian Root Lab",
              "host": "173.255.205.169",
              "port": 22,
              "username": "root",
              "passwordEnvironmentVariable": "MCPSERVER_SSH_VAULT_DEBIAN_ROOT_LAB_PASSWORD",
              "hostKeySha256": "SHA256:Q7mMEDNG2w/v+PBa0ogNmW3ECGDGapU2NFgKRX5/5yI",
              "acceptUnknownHostKey": false,
              "workingDirectory": "/root",
              "allowedCommands": ["whoami"],
              "allowedRemotePathPrefixes": ["/root"],
              "allowAllCommands": true,
              "privileged": true,
              "allowedRoot": true
            }
          }
        }
        """, TestContext.Current.CancellationToken);

        var sut = temp.CreateStore();

        var catalog = TestFin.Success(await sut.LoadProfilesAsync(TestContext.Current.CancellationToken));

        var profile = Assert.Single(catalog.Profiles.Values);
        Assert.Equal("debian-root-lab", profile.Name);
        Assert.Equal("Debian Root Lab", profile.DisplayName);
        Assert.Equal("173.255.205.169", profile.Host);
        Assert.Equal("root", profile.Username);
        Assert.Equal("MCPSERVER_SSH_VAULT_DEBIAN_ROOT_LAB_PASSWORD", profile.PasswordEnvironmentVariable);
        Assert.Equal("SHA256:Q7mMEDNG2w/v+PBa0ogNmW3ECGDGapU2NFgKRX5/5yI", profile.HostKeySha256);
        Assert.True(profile.AllowAllCommands);
        Assert.True(profile.Privileged);
        Assert.True(profile.AllowedRoot);
        Assert.Equal(new[] { "whoami" }, profile.AllowedCommands);
        Assert.Equal(new[] { "/root" }, profile.AllowedRemotePathPrefixes);
        Assert.StartsWith("sqlite:", profile.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadProfilesAsync_Uses_Sqlite_As_Canonical_Store_After_Import()
    {
        await using var temp = TempSshProfileStore.Create();
        await temp.WriteProfilesJsonAsync("""
        {
          "profiles": {
            "dev": {
              "host": "192.168.1.50",
              "port": 22,
              "username": "james",
              "passwordEnvironmentVariable": "MCPSERVER_SSH_VAULT_DEV_PASSWORD",
              "allowedCommands": ["dotnet", "git"]
            }
          }
        }
        """, TestContext.Current.CancellationToken);

        var sut = temp.CreateStore();
        _ = TestFin.Success(await sut.LoadProfilesAsync(TestContext.Current.CancellationToken));

        File.Delete(temp.ProfileJsonPath);

        var reloaded = TestFin.Success(await sut.LoadProfilesAsync(TestContext.Current.CancellationToken));

        Assert.True(reloaded.Profiles.TryGetValue("dev", out var profile));
        Assert.Equal("192.168.1.50", profile.Host);
        Assert.Equal(new[] { "dotnet", "git" }, profile.AllowedCommands);
        Assert.StartsWith("sqlite:", profile.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadProfilesAsync_Can_Be_Configured_For_Legacy_Json_Profile_Store()
    {
        await using var temp = TempSshProfileStore.Create(profileStoreKind: SshProfileStoreKinds.Json);
        await temp.WriteProfilesJsonAsync("""
        {
          "profiles": {
            "dev-key": {
              "host": "192.168.1.50",
              "port": 22,
              "username": "james",
              "privateKeyPath": "C:\\Users\\James Arceri\\.ssh\\id_ed25519"
            }
          }
        }
        """, TestContext.Current.CancellationToken);

        var fileStore = temp.CreateJsonStore();

        var catalog = TestFin.Success(await fileStore.LoadProfilesAsync(TestContext.Current.CancellationToken));

        var profile = Assert.Single(catalog.Profiles.Values);
        Assert.Equal("dev-key", profile.Name);
        Assert.Equal("C:\\Users\\James Arceri\\.ssh\\id_ed25519", profile.PrivateKeyPath);
        Assert.Equal(temp.ProfileJsonPath, profile.Source);
    }

    private sealed class TempSshProfileStore : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly StaticOptionsMonitor<SshToolSettings> _settings;
        private readonly TestSshPathResolver _pathResolver;

        private TempSshProfileStore(string directory, string profileStoreKind)
        {
            _directory = directory;
            ProfileJsonPath = Path.Combine(directory, "ssh-profiles.local.json");
            DatabasePath = Path.Combine(directory, "ssh-store.db");
            _settings = new StaticOptionsMonitor<SshToolSettings>(new SshToolSettings
            {
                Enabled = true,
                ProfileStoreKind = profileStoreKind,
                ProfilePath = ProfileJsonPath,
                ProfileDatabasePath = DatabasePath,
                ImportProfilesFromJsonOnEmpty = true
            });
            _pathResolver = new TestSshPathResolver(directory);
        }

        public string ProfileJsonPath { get; }

        public string DatabasePath { get; }

        public static TempSshProfileStore Create(string profileStoreKind = SshProfileStoreKinds.Sqlite)
        {
            var directory = Path.Combine(Path.GetTempPath(), "mcpserver-ssh-profiles-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new TempSshProfileStore(directory, profileStoreKind);
        }

        public async ValueTask WriteProfilesJsonAsync(string json, CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(ProfileJsonPath, json, cancellationToken).ConfigureAwait(false);
        }

        public FileSystemSshProfileStore CreateJsonStore()
        {
            return new FileSystemSshProfileStore(
                _settings,
                _pathResolver,
                NullLogger<FileSystemSshProfileStore>.Instance);
        }

        public SqliteSshProfileStore CreateStore()
        {
            return new SqliteSshProfileStore(
                _settings,
                _pathResolver,
                CreateJsonStore(),
                NullLogger<SqliteSshProfileStore>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            await DeleteDirectoryWithRetryAsync(_directory, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask DeleteDirectoryWithRetryAsync(string directory, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            const int maxAttempts = 20;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts - 1)
                {
                    SqliteConnection.ClearAllPools();
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts - 1)
                {
                    SqliteConnection.ClearAllPools();
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private sealed class TestSshPathResolver : ISshPathResolver
    {
        private readonly string _root;

        public TestSshPathResolver(string root)
        {
            _root = root;
        }

        public string ResolveConfiguredPath(string path)
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            return Path.IsPathRooted(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(_root, expanded));
        }

        public string ResolveContentPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(_root, relativePath));
        }

        public string ResolveUserDataPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(_root, relativePath));
        }

        public string ResolveLegacyRoamingUserDataPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(_root, "roaming", relativePath));
        }
    }

    private sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        private readonly TOptions _value;

        public StaticOptionsMonitor(TOptions value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public TOptions CurrentValue => _value;

        public TOptions Get(string? name)
        {
            return _value;
        }

        public IDisposable? OnChange(Action<TOptions, string?> listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            return NullDisposable.Instance;
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            private NullDisposable()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
