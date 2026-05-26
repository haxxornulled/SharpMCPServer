using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;

namespace MCPServer.Tools.Ssh.Tools;

public sealed class SshProfilesListTool : IMcpTool
{
    private static readonly JsonElement InputSchema = CreateInputSchema();
    private static readonly JsonElement OutputSchema = CreateOutputSchema();

    private readonly ISshProfileStore _profileStore;
    private readonly ISshCredentialResolver _credentialResolver;

    public SshProfilesListTool(
        ISshProfileStore profileStore,
        ISshCredentialResolver credentialResolver)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = SshToolNames.ProfilesList,
        Title = "List SSH Profiles",
        Description = "Lists configured SSH profiles without exposing secrets.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution
        {
            TaskSupport = McpToolTaskSupport.Forbidden
        }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments is { ValueKind: JsonValueKind.Object } suppliedArguments)
        {
            using var properties = suppliedArguments.EnumerateObject();
            if (properties.MoveNext())
            {
                return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{SshToolNames.ProfilesList} does not accept arguments.", isError: true));
            }
        }

        var loaded = await _profileStore.LoadProfilesAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.IsFail)
        {
            return loaded.Match<Fin<ToolCallResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH profile load success while handling failure."),
                Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
        }

        var catalog = loaded.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH profile load failure while handling success."));

        var credentialAvailability = await BuildCredentialAvailabilityAsync(catalog, cancellationToken).ConfigureAwait(false);
        var structuredContent = SshJson.ToProfilesJsonElement(catalog, credentialAvailability);
        var text = catalog.Profiles.Count == 1
            ? "1 SSH profile configured."
            : $"{catalog.Profiles.Count} SSH profiles configured.";

        return Fin.Succ<ToolCallResult>(ToolCallResult.Text(text, structuredContent: structuredContent));
    }


    private async ValueTask<IReadOnlyDictionary<string, bool>> BuildCredentialAvailabilityAsync(
        SshProfileCatalog catalog,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in catalog.Profiles.Values)
        {
            if (profile.PasswordEnvironmentVariable is { Length: > 0 } passwordReference)
            {
                result[passwordReference] = await _credentialResolver.HasSecretAsync(passwordReference, cancellationToken).ConfigureAwait(false);
            }

            if (profile.PrivateKeyPassphraseEnvironmentVariable is { Length: > 0 } passphraseReference)
            {
                result[passphraseReference] = await _credentialResolver.HasSecretAsync(passphraseReference, cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }

    private static JsonElement CreateInputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false
        }
        """);

        return document.RootElement.Clone();
    }

    private static JsonElement CreateOutputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["profiles", "sources"],
          "properties": {
            "profiles": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["name", "displayName", "host", "port", "username", "credentialKind", "hasCredentialConfigured", "passwordEnvironmentVariableSet", "hostKeyPinned", "acceptUnknownHostKey", "allowAllCommands", "privileged", "allowedRoot", "allowedCommands", "allowedRemotePathPrefixes", "source"],
                "properties": {
                  "name": { "type": "string" },
                  "displayName": { "type": "string" },
                  "host": { "type": "string" },
                  "port": { "type": "integer" },
                  "username": { "type": "string" },
                  "credentialKind": { "type": "string", "enum": ["none", "private-key", "password-environment-variable", "multiple"] },
                  "hasCredentialConfigured": { "type": "boolean" },
                  "passwordEnvironmentVariableSet": { "type": "boolean" },
                  "hostKeyPinned": { "type": "boolean" },
                  "acceptUnknownHostKey": { "type": "boolean" },
                  "allowAllCommands": { "type": "boolean" },
                  "privileged": { "type": "boolean" },
                  "allowedRoot": { "type": "boolean" },
                  "allowedCommands": { "type": "array", "items": { "type": "string" } },
                  "allowedRemotePathPrefixes": { "type": "array", "items": { "type": "string" } },
                  "source": { "type": "string" }
                },
                "additionalProperties": false
              }
            },
            "sources": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["path", "exists", "profileCount"],
                "properties": {
                  "path": { "type": "string" },
                  "exists": { "type": "boolean" },
                  "profileCount": { "type": "integer" },
                  "error": { "type": "string" }
                },
                "additionalProperties": false
              }
            }
          },
          "additionalProperties": false
        }
        """);

        return document.RootElement.Clone();
    }
}
