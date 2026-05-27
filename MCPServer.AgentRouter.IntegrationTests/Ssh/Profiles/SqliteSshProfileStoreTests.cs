using MCPServer.ExecutionPlugins.Ssh.Tests.Testing;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Infrastructure;
using MCPServer.Ssh.Models;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace MCPServer.AgentRouter.IntegrationTests.Ssh.Profiles;

public sealed class SqliteSshProfileStoreTests
{
    [Fact]
    public async Task LoadProfilesAsync_Returns_Empty_Catalog_When_Database_Is_Empty()
    {
        await using var temp = TempSshProfileStore.Create();
        var sut = temp.CreateStore();

        var catalog = TestFin.Success(await sut.LoadProfilesAsync(TestContext.Current.CancellationToken));

        Assert.Empty(catalog.Profiles);
        Assert.Single(catalog.Sources);
        Assert.Equal(temp.DatabasePath, catalog.Sources[0].Path);
        Assert.True(catalog.Sources[0].Exists);
        Assert.Equal(0, catalog.Sources[0].ProfileCount);
    }

    [Fact]
    public async Task UpsertProfileAsync_Persists_To_SQLite_And_LoadProfilesAsync_Returns_Data()
    {
        await using var temp = TempSshProfileStore.Create();
        var sut = temp.CreateStore();

        var upserted = TestFin.Success(await sut.UpsertProfileAsync(
            "dev",
            new SshProfileUpsertRequest
            {
                DisplayName = "Dev Lab",
                Host = "192.168.1.50",
                Port = 22,
                Username = "james",
                PasswordCredentialReference = "ssh/profile/dev/password",
                AllowedCommands =
                [
                    "dotnet",
                    "git"
                ]
            },
            TestContext.Current.CancellationToken));

        Assert.Equal("dev", upserted.Name);
        Assert.Equal("Dev Lab", upserted.DisplayName);
        Assert.Equal("192.168.1.50", upserted.Host);

        var catalog = TestFin.Success(await sut.LoadProfilesAsync(TestContext.Current.CancellationToken));

        Assert.True(catalog.Profiles.TryGetValue("dev", out var profile));
        Assert.Equal("Dev Lab", profile.DisplayName);
        Assert.Equal("192.168.1.50", profile.Host);
        Assert.Equal("james", profile.Username);
        Assert.Equal("ssh/profile/dev/password", profile.PasswordCredentialReference);
        Assert.Equal(new[] { "dotnet", "git" }, profile.AllowedCommands);
        Assert.StartsWith("sqlite:", profile.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplaceProfileAsync_Returns_Failure_When_Profile_Does_Not_Exist()
    {
        await using var temp = TempSshProfileStore.Create();
        var sut = temp.CreateStore();

        var result = await sut.ReplaceProfileAsync(
            "missing",
            new SshProfileUpsertRequest
            {
                Host = "192.168.1.50",
                Username = "james"
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => string.Empty,
            Fail: failure => failure.Message);
        Assert.Contains("was not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpsertProfileAsync_Handles_Parallel_Writes_Without_Losing_Data()
    {
        await using var temp = TempSshProfileStore.Create();
        var sut = temp.CreateStore();

        var operations = Enumerable.Range(0, 24)
            .Select(index => sut.UpsertProfileAsync(
                $"dev-{index}",
                new SshProfileUpsertRequest
                {
                    DisplayName = $"Dev {index}",
                    Host = "127.0.0.1",
                    Port = 22,
                    Username = "james",
                    AllowedCommands =
                    [
                        "dotnet",
                        "git"
                    ]
                },
                TestContext.Current.CancellationToken)
                .AsTask())
            .ToArray();

        await Task.WhenAll(operations);

        var catalog = TestFin.Success(await sut.LoadProfilesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(24, catalog.Profiles.Count);
        Assert.All(catalog.Profiles.Values, profile =>
        {
            Assert.Equal("127.0.0.1", profile.Host);
            Assert.Equal(new[] { "dotnet", "git" }, profile.AllowedCommands);
        });
    }

    private sealed class TempSshProfileStore : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly StaticOptionsMonitor<SshToolSettings> _settings;
        private readonly TestSshPathResolver _pathResolver;

        private TempSshProfileStore(string directory)
        {
            _directory = directory;
            DatabasePath = Path.Combine(directory, "ssh-store.db");
            _settings = new StaticOptionsMonitor<SshToolSettings>(new SshToolSettings
            {
                Enabled = true,
                ProfileDatabasePath = DatabasePath
            });
            _pathResolver = new TestSshPathResolver(directory);
        }

        public string DatabasePath { get; }

        public static TempSshProfileStore Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "mcpserver-ssh-profiles-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new TempSshProfileStore(directory);
        }

        public SqliteSshProfileStore CreateStore()
        {
            return new SqliteSshProfileStore(
                _settings,
                _pathResolver);
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
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(_root, path));
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
