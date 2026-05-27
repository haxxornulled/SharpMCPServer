using System.Text.Json;
using LanguageExt;
using MCPServer.ExecutionPlugins.Ssh.Tests.Testing;
using MCPServer.Domain.Mcp;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Interfaces;
using MCPServer.Tools.Ssh.Tools;
using Xunit;

namespace MCPServer.AgentRouter.IntegrationTests.Ssh.Tools.Testing;

internal sealed class SshProfilesListToolSut
{
    private readonly Dictionary<string, SshProfileDefinition> _profiles;
    private readonly SshProfileCatalog _catalog;
    private readonly FakeSshCredentialResolver _credentialResolver;
    private readonly SshProfilesListTool _tool;

    public SshProfilesListToolSut()
    {
        _profiles = new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        _catalog = new SshProfileCatalog
        {
            Profiles = _profiles
        };
        _credentialResolver = new FakeSshCredentialResolver();
        _tool = new SshProfilesListTool(new FakeSshProfileStore(_catalog), _credentialResolver);
    }

    public SshProfilesListTool Tool => _tool;

    public SshProfilesListToolSut AddAvailableCredential(string credentialReference)
    {
        _credentialResolver.AddAvailableCredential(credentialReference);
        return this;
    }

    public SshProfilesListToolSut AddProfile(SshProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Name is not { Length: > 0 } name)
        {
            throw new ArgumentException("SSH test profiles must have a non-empty name.", nameof(profile));
        }

        _profiles[name] = profile;
        return this;
    }

    public async ValueTask<ToolCallResult> ExecuteAsync(
        JsonElement? arguments = null,
        CancellationToken cancellationToken = default)
    {
        return TestFin.Success(await _tool.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false));
    }

    public async ValueTask<ToolCallResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(arguments: null, cancellationToken).ConfigureAwait(false);
    }

    public static JsonElement Profiles(ToolCallResult result)
    {
        Assert.False(result.IsError);
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent.Value.GetProperty("profiles");
    }

    public static JsonElement SingleProfile(ToolCallResult result)
    {
        return Assert.Single(Profiles(result).EnumerateArray());
    }

    private sealed class FakeSshCredentialResolver : ISshCredentialResolver
    {
        private readonly System.Collections.Generic.HashSet<string> _availableCredentials = new(StringComparer.OrdinalIgnoreCase);

        public void AddAvailableCredential(string credentialReference)
        {
            _availableCredentials.Add(credentialReference);
        }

        public ValueTask<string?> ResolveSecretAsync(string? credentialReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<string?>(credentialReference is { Length: > 0 } reference && _availableCredentials.Contains(reference)
                ? "available-test-secret"
                : null);
        }

        public ValueTask<bool> HasSecretAsync(string? credentialReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<bool>(credentialReference is { Length: > 0 } reference && _availableCredentials.Contains(reference));
        }
    }

    private sealed class FakeSshProfileStore : ISshProfileStore
    {
        private readonly SshProfileCatalog _catalog;

        public FakeSshProfileStore(SshProfileCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<SshProfileCatalog>>(Fin.Succ(_catalog));
        }
    }
}
