using System.Text.Json.Serialization;

namespace MCPServer.AgentRouter.Abstractions.Models;

public readonly record struct AgentRouterBridgeResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("runId")] string? RunId,
    [property: JsonPropertyName("startedAtUtc")] DateTimeOffset StartedAtUtc,
    [property: JsonPropertyName("completedAtUtc")] DateTimeOffset CompletedAtUtc);
