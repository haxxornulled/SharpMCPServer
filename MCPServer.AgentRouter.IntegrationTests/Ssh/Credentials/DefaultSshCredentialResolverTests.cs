using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Infrastructure;
using MCPServer.Ssh.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MCPServer.AgentRouter.IntegrationTests.Ssh.Credentials;

public sealed class DefaultSshCredentialResolverTests
{
    [Fact]
    public async Task ResolveSecretAsync_Returns_Sqlite_Vault_Value_When_Reference_Is_Generated_Credential_Alias()
    {
        await using var temp = TempSshCredentialDatabase.Create();
        var vault = temp.CreateVault();
        await vault.UpsertEntryAsync(
            "dev-admin-password",
            "from-sqlite",
            "Password for SSH profile 'dev-admin'",
            TestContext.Current.CancellationToken);
        var resolver = temp.CreateResolver();

        var secret = await resolver.ResolveSecretAsync(
            "MCPSERVER_SSH_VAULT_DEV_ADMIN_PASSWORD",
            TestContext.Current.CancellationToken);

        Assert.Equal("from-sqlite", secret);
    }

    [Fact]
    public async Task ResolveSecretAsync_Returns_Sqlite_Vault_Value_When_Reference_Is_Item_Name()
    {
        await using var temp = TempSshCredentialDatabase.Create();
        var vault = temp.CreateVault();
        await vault.UpsertEntryAsync(
            "dev-admin-password",
            "from-item-name",
            null,
            TestContext.Current.CancellationToken);
        var resolver = temp.CreateResolver();

        var secret = await resolver.ResolveSecretAsync(
            "dev-admin-password",
            TestContext.Current.CancellationToken);

        Assert.Equal("from-item-name", secret);
    }

    [Fact]
    public async Task ResolveSecretAsync_Prefers_Sqlite_Vault_Over_Process_Environment_Fallback()
    {
        const string credentialReference = "MCPSERVER_SSH_VAULT_DEV_ADMIN_PASSWORD";
        var originalValue = Environment.GetEnvironmentVariable(credentialReference);
        try
        {
            Environment.SetEnvironmentVariable(credentialReference, "from-env");
            await using var temp = TempSshCredentialDatabase.Create();
            var vault = temp.CreateVault();
            await vault.UpsertEntryAsync(
                "dev-admin-password",
                "from-sqlite",
                null,
                TestContext.Current.CancellationToken);
            var resolver = temp.CreateResolver();

            var secret = await resolver.ResolveSecretAsync(credentialReference, TestContext.Current.CancellationToken);

            Assert.Equal("from-sqlite", secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(credentialReference, originalValue);
        }
    }

    [Fact]
    public async Task ResolveSecretAsync_Returns_Process_Environment_Value_As_Compatibility_Fallback()
    {
        const string credentialReference = "MCPSERVER_TEST_DIRECT_ENV_SECRET";
        var originalValue = Environment.GetEnvironmentVariable(credentialReference);
        try
        {
            Environment.SetEnvironmentVariable(credentialReference, "from-env");
            await using var temp = TempSshCredentialDatabase.Create();
            var resolver = temp.CreateResolver();

            var secret = await resolver.ResolveSecretAsync(credentialReference, TestContext.Current.CancellationToken);

            Assert.Equal("from-env", secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(credentialReference, originalValue);
        }
    }

    [Fact]
    public async Task HasSecretAsync_Returns_False_When_Secret_Is_Not_In_Sqlite_Or_Environment_Fallback()
    {
        const string credentialReference = "MCPSERVER_SSH_VAULT_MISSING_PASSWORD";
        var originalValue = Environment.GetEnvironmentVariable(credentialReference);
        try
        {
            Environment.SetEnvironmentVariable(credentialReference, null);
            await using var temp = TempSshCredentialDatabase.Create();
            var resolver = temp.CreateResolver();

            var hasSecret = await resolver.HasSecretAsync(credentialReference, TestContext.Current.CancellationToken);

            Assert.False(hasSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(credentialReference, originalValue);
        }
    }

    [Fact]
    public async Task SqliteVault_Handles_Parallel_Writes_And_Reads()
    {
        await using var temp = TempSshCredentialDatabase.Create();
        var vault = temp.CreateVault();

        var upserts = Enumerable.Range(0, 24)
            .Select(index => vault.UpsertEntryAsync(
                $"secret-{index}",
                $"value-{index}",
                $"entry {index}",
                TestContext.Current.CancellationToken)
                .AsTask())
            .ToArray();

        await Task.WhenAll(upserts);

        var entries = await vault.ListEntriesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(24, entries.Count);

        var resolved = await Task.WhenAll(
            Enumerable.Range(0, 24).Select(index => vault.ResolveSecretAsync(
                $"secret-{index}",
                TestContext.Current.CancellationToken)
                .AsTask()));

        Assert.Equal(Enumerable.Range(0, 24).Select(index => $"value-{index}"), resolved);
    }

    private sealed class TempSshCredentialDatabase : IAsyncDisposable
    {
        private readonly string _root;

        private TempSshCredentialDatabase(string root)
        {
            _root = root;
            DatabasePath = Path.Combine(root, "ssh-store.db");
        }

        public string DatabasePath { get; }

        public static TempSshCredentialDatabase Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "mcp-ssh-credential-db-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TempSshCredentialDatabase(root);
        }

        public SqliteSshCredentialVault CreateVault()
        {
            return new SqliteSshCredentialVault(CreateOptionsMonitor(), new TestPathResolver(_root));
        }

        public DefaultSshCredentialResolver CreateResolver()
        {
            return new DefaultSshCredentialResolver(
                CreateVault(),
                NullLogger<DefaultSshCredentialResolver>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (Directory.Exists(_root))
                    {
                        Directory.Delete(_root, recursive: true);
                    }

                    return;
                }
                catch when (attempt < 9)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
                }
            }
        }

        private StaticOptionsMonitor<SshToolSettings> CreateOptionsMonitor()
        {
            return new StaticOptionsMonitor<SshToolSettings>(new SshToolSettings
            {
                ProfileDatabasePath = DatabasePath,
                UseLocalCredentialVault = true
            });
        }
    }

    private sealed class TestPathResolver : MCPServer.Ssh.Interfaces.ISshPathResolver
    {
        private readonly string _root;

        public TestPathResolver(string root)
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

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;

        public StaticOptionsMonitor(T value)
        {
            _value = value;
        }

        public T CurrentValue => _value;

        public T Get(string? name) => _value;

        public IDisposable? OnChange(Action<T, string?> listener)
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
