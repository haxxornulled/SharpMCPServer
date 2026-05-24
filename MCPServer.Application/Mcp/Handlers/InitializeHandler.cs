using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class InitializeHandler : IMcpMethodHandler
{
    private readonly IMcpSessionState _sessionState;

    public InitializeHandler(IMcpSessionState sessionState)
    {
        ArgumentNullException.ThrowIfNull(sessionState);
        _sessionState = sessionState;
    }

    public string Method => McpMethods.Initialize;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is not { } initializeParameters)
        {
            return Fail("Initialize parameters are required.");
        }

        InitializeRequest? request;
        try
        {
            request = initializeParameters.Deserialize(McpJsonSerializerContext.Default.InitializeRequest);
        }
        catch (JsonException ex)
        {
            return Fail($"Initialize parameters are invalid JSON: {ex.Message}");
        }

        if (request is not { ClientInfo: { } clientInfo })
        {
            return Fail("Initialize parameters are invalid.");
        }

        if (ValidateInitializeRequest(initializeParameters, request, clientInfo) is { } validationFailure)
        {
            return Fail(validationFailure);
        }

        if (!McpClientCapabilityState.TryRead(request.Capabilities, out var clientCapabilities, out var capabilityError))
        {
            return Fail(capabilityError);
        }

        var negotiatedVersion = McpProtocolVersions.IsSupported(request.ProtocolVersion)
            ? request.ProtocolVersion
            : McpProtocolVersions.Current;

        _sessionState.MarkInitializeResponseSent(negotiatedVersion, clientInfo, clientCapabilities);

        var result = new InitializeResult
        {
            ProtocolVersion = negotiatedVersion,
            Capabilities = new McpServerCapabilities
            {
                Tools = new McpToolsCapability
                {
                    ListChanged = false
                },
                Logging = new McpLoggingCapability(),
                Resources = new McpResourcesCapability
                {
                    Subscribe = true
                },
                Prompts = new McpPromptsCapability
                {
                    ListChanged = false
                },
                Completions = new McpCompletionsCapability()
            },
            ServerInfo = new McpImplementationInfo
            {
                Name = "MCPServer",
                Title = "C# MCP Server",
                Version = "0.1.0-phase1",
                Description = "A C# Model Context Protocol server targeting MCP 2025-11-25."
            },
            Instructions = "MCP 2025-11-25 stdio server with tools, resources, prompts, completions, logging, and resource subscriptions."
        };

        var payload = JsonSerializer.SerializeToElement(result, McpJsonSerializerContext.Default.InitializeResult);
        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(payload));
    }

    private static string? ValidateInitializeRequest(JsonElement parameters, InitializeRequest request, McpImplementationInfo clientInfo)
    {
        if (!parameters.TryGetProperty("protocolVersion"u8, out var protocolVersionElement) ||
            protocolVersionElement is not { ValueKind: JsonValueKind.String } ||
            string.IsNullOrWhiteSpace(request.ProtocolVersion))
        {
            return "Initialize protocolVersion is required.";
        }

        if (!parameters.TryGetProperty("capabilities"u8, out var capabilitiesElement) ||
            capabilitiesElement is not { ValueKind: JsonValueKind.Object })
        {
            return "Initialize capabilities must be a JSON object.";
        }

        if (!parameters.TryGetProperty("clientInfo"u8, out var clientInfoElement) ||
            clientInfoElement is not { ValueKind: JsonValueKind.Object })
        {
            return "Initialize clientInfo must be a JSON object.";
        }

        if (!clientInfoElement.TryGetProperty("name"u8, out var nameElement) ||
            nameElement is not { ValueKind: JsonValueKind.String } ||
            string.IsNullOrWhiteSpace(clientInfo.Name))
        {
            return "Initialize clientInfo.name is required.";
        }

        if (!clientInfoElement.TryGetProperty("version"u8, out var versionElement) ||
            versionElement is not { ValueKind: JsonValueKind.String } ||
            string.IsNullOrWhiteSpace(clientInfo.Version))
        {
            return "Initialize clientInfo.version is required.";
        }

        return default;
    }

    private static ValueTask<Fin<JsonElement>> Fail(string message)
    {
        return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New(message)));
    }
}
