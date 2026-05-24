namespace MCPServer.AgentRouter.Abstractions.Models;

public readonly record struct AgentRouterRunResult(
    string Status,
    string Message,
    string? RunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
