using System.Text.Json;

namespace MCPServer.Tools.Ssh.Models;

public sealed class SshAgentLaunchRequest
{
    public string Profile { get; init; } = string.Empty;

    public string Objective { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    public int? TimeoutSecondsPerStep { get; init; }

    public string? OperationKey { get; init; }

    public IReadOnlyList<SshAgentCommandRequest> Commands { get; init; } = Array.Empty<SshAgentCommandRequest>();

    public static SshAgentLaunchRequest FromArguments(JsonElement? arguments)
    {
        var root = arguments ?? default;
        if (root.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new SshAgentLaunchRequest();
        }

        var commands = new List<SshAgentCommandRequest>();
        if (root.TryGetProperty("commands"u8, out var commandsElement) && commandsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in commandsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var args = new List<string>();
                if (item.TryGetProperty("arguments"u8, out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var arg in argumentsElement.EnumerateArray())
                    {
                        if (arg.ValueKind == JsonValueKind.String)
                        {
                            args.Add(arg.GetString() ?? string.Empty);
                        }
                    }
                }

                commands.Add(new SshAgentCommandRequest
                {
                    Command = item.TryGetProperty("command"u8, out var commandElement) && commandElement.ValueKind == JsonValueKind.String
                        ? commandElement.GetString() ?? string.Empty
                        : string.Empty,
                    Arguments = args.ToArray(),
                    WorkingDirectory = item.TryGetProperty("workingDirectory"u8, out var workingDirectoryElement) && workingDirectoryElement.ValueKind == JsonValueKind.String
                        ? workingDirectoryElement.GetString()
                        : null,
                    TimeoutSeconds = item.TryGetProperty("timeoutSeconds"u8, out var timeoutElement) && timeoutElement.ValueKind == JsonValueKind.Number && timeoutElement.TryGetInt32(out var timeout)
                        ? timeout
                        : null
                });
            }
        }

        return new SshAgentLaunchRequest
        {
            Profile = root.TryGetProperty("profile"u8, out var profile) && profile.ValueKind == JsonValueKind.String
                ? profile.GetString() ?? string.Empty
                : string.Empty,
            Objective = root.TryGetProperty("objective"u8, out var objective) && objective.ValueKind == JsonValueKind.String
                ? objective.GetString() ?? string.Empty
                : string.Empty,
            WorkingDirectory = root.TryGetProperty("workingDirectory"u8, out var workingDirectory) && workingDirectory.ValueKind == JsonValueKind.String
                ? workingDirectory.GetString()
                : null,
            TimeoutSecondsPerStep = root.TryGetProperty("timeoutSecondsPerStep"u8, out var timeoutSecondsPerStep) && timeoutSecondsPerStep.ValueKind == JsonValueKind.Number && timeoutSecondsPerStep.TryGetInt32(out var timeoutValue)
                ? timeoutValue
                : null,
            OperationKey = root.TryGetProperty("operationKey"u8, out var operationKey) && operationKey.ValueKind == JsonValueKind.String
                ? operationKey.GetString()
                : null,
            Commands = commands.ToArray()
        };
    }
}

public sealed class SshAgentCommandRequest
{
    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string? WorkingDirectory { get; init; }

    public int? TimeoutSeconds { get; init; }
}

public sealed class SshAgentLaunchResponse
{
    public string AgentId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Profile { get; init; } = string.Empty;

    public string Objective { get; init; } = string.Empty;

    public int CommandCount { get; init; }

    public int CurrentStep { get; init; }

    public int PollIntervalMilliseconds { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastUpdatedAt { get; init; }

    public string Summary { get; init; } = string.Empty;
}

public sealed class SshAgentStatusResponse
{
    public string AgentId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Profile { get; init; } = string.Empty;

    public string Objective { get; init; } = string.Empty;

    public int CommandCount { get; init; }

    public int CurrentStep { get; init; }

    public int CompletedSteps { get; init; }

    public int FailedSteps { get; init; }

    public bool CancellationRequested { get; init; }

    public string CurrentCommand { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string StdoutTail { get; init; } = string.Empty;

    public string StderrTail { get; init; } = string.Empty;

    public int StdoutLength { get; init; }

    public int StderrLength { get; init; }

    public IReadOnlyList<SshAgentStepSnapshot> Steps { get; init; } = Array.Empty<SshAgentStepSnapshot>();

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastUpdatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed class SshAgentOutputRequest
{
    public string AgentId { get; init; } = string.Empty;

    public int StdoutOffset { get; init; }

    public int StderrOffset { get; init; }

    public int MaxChars { get; init; } = 20000;

    public static SshAgentOutputRequest FromArguments(JsonElement? arguments)
    {
        var root = arguments ?? default;
        if (root.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new SshAgentOutputRequest();
        }

        return new SshAgentOutputRequest
        {
            AgentId = root.TryGetProperty("agentId"u8, out var agentId) && agentId.ValueKind == JsonValueKind.String
                ? agentId.GetString() ?? string.Empty
                : string.Empty,
            StdoutOffset = root.TryGetProperty("stdoutOffset"u8, out var stdoutOffset) && stdoutOffset.ValueKind == JsonValueKind.Number && stdoutOffset.TryGetInt32(out var stdout)
                ? Math.Max(0, stdout)
                : 0,
            StderrOffset = root.TryGetProperty("stderrOffset"u8, out var stderrOffset) && stderrOffset.ValueKind == JsonValueKind.Number && stderrOffset.TryGetInt32(out var stderr)
                ? Math.Max(0, stderr)
                : 0,
            MaxChars = root.TryGetProperty("maxChars"u8, out var maxChars) && maxChars.ValueKind == JsonValueKind.Number && maxChars.TryGetInt32(out var max)
                ? Math.Clamp(max, 1, 100000)
                : 20000
        };
    }
}

public sealed class SshAgentOutputResponse
{
    public string AgentId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Stdout { get; init; } = string.Empty;

    public string Stderr { get; init; } = string.Empty;

    public int StdoutOffset { get; init; }

    public int StderrOffset { get; init; }

    public int NextStdoutOffset { get; init; }

    public int NextStderrOffset { get; init; }

    public bool StdoutTruncated { get; init; }

    public bool StderrTruncated { get; init; }
}

public sealed class SshAgentCancelResponse
{
    public string AgentId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public bool CancellationRequested { get; init; }

    public string Summary { get; init; } = string.Empty;

    public DateTimeOffset LastUpdatedAt { get; init; }
}

public sealed class SshAgentStepSnapshot
{
    public int Index { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public int? ExitCode { get; init; }

    public string Summary { get; init; } = string.Empty;

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

public static class SshAgentStatusNames
{
    public const string Queued = "queued";
    public const string Working = "working";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
