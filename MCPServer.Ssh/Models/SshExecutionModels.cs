using System.Text.Json;

namespace MCPServer.Ssh.Models;

public sealed class SshExecutionRequest
{
    public string Profile { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string? WorkingDirectory { get; init; }

    public int? TimeoutSeconds { get; init; }

    public string? OperationKey { get; init; }

    public static SshExecutionRequest FromArguments(JsonElement? arguments)
    {
        var root = arguments ?? default;
        if (root.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new SshExecutionRequest();
        }

        var values = new SshExecutionRequestBuilder();

        if (root.TryGetProperty("profile"u8, out var profile) && profile.ValueKind == JsonValueKind.String)
        {
            values.Profile = profile.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("command"u8, out var command) && command.ValueKind == JsonValueKind.String)
        {
            values.Command = command.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("arguments"u8, out var args) && args.ValueKind == JsonValueKind.Array)
        {
            var buffer = new List<string>();
            foreach (var item in args.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    buffer.Add(item.GetString() ?? string.Empty);
                }
            }

            values.Arguments = buffer.ToArray();
        }

        if (root.TryGetProperty("workingDirectory"u8, out var workingDirectory) && workingDirectory.ValueKind == JsonValueKind.String)
        {
            values.WorkingDirectory = workingDirectory.GetString();
        }

        if (root.TryGetProperty("timeoutSeconds"u8, out var timeoutSeconds) && timeoutSeconds.ValueKind == JsonValueKind.Number && timeoutSeconds.TryGetInt32(out var timeoutValue))
        {
            values.TimeoutSeconds = timeoutValue;
        }

        if (root.TryGetProperty("operationKey"u8, out var operationKey) && operationKey.ValueKind == JsonValueKind.String)
        {
            values.OperationKey = operationKey.GetString();
        }

        return new SshExecutionRequest
        {
            Profile = values.Profile,
            Command = values.Command,
            Arguments = values.Arguments,
            WorkingDirectory = values.WorkingDirectory,
            TimeoutSeconds = values.TimeoutSeconds,
            OperationKey = values.OperationKey
        };
    }

    private sealed class SshExecutionRequestBuilder
    {
        public string Profile { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

        public string? WorkingDirectory { get; set; }

        public int? TimeoutSeconds { get; set; }

        public string? OperationKey { get; set; }
    }
}

public sealed class SshExecutionPolicyDecision
{
    public bool Allowed { get; init; }

    public string Decision { get; init; } = string.Empty;

    public string? Reason { get; init; }

    public string ProfileName { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 22;

    public string Username { get; init; } = string.Empty;

    public string ResolvedCommand { get; init; } = string.Empty;

    public IReadOnlyList<string> ResolvedArguments { get; init; } = Array.Empty<string>();

    public string WorkingDirectory { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 60;

    public int MaxOutputChars { get; init; } = 20_000;

    public string? PrivateKeyPath { get; init; }

    public string? PrivateKeyPassphraseEnvironmentVariable { get; init; }

    public string? PasswordEnvironmentVariable { get; init; }

    public string? HostKeySha256 { get; init; }

    public bool AcceptUnknownHostKey { get; init; }
}

public sealed class SshExecutionCommand
{
    public string ProfileName { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 22;

    public string Username { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string WorkingDirectory { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 60;

    public int MaxOutputChars { get; init; } = 20_000;

    public string? PrivateKeyPath { get; init; }

    public string? PrivateKeyPassphraseEnvironmentVariable { get; init; }

    public string? PasswordEnvironmentVariable { get; init; }

    public string? HostKeySha256 { get; init; }

    public bool AcceptUnknownHostKey { get; init; }

    public string? OperationKey { get; init; }
}

public sealed class SshCommandExecutionResult
{
    public int ExitCode { get; init; }

    public bool TimedOut { get; init; }

    public string Stdout { get; init; } = string.Empty;

    public string Stderr { get; init; } = string.Empty;

    public bool StdoutTruncated { get; init; }

    public bool StderrTruncated { get; init; }

    public long ElapsedMilliseconds { get; init; }
}

public sealed class SshExecutionResponse
{
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public bool Allowed { get; init; }

    public string PolicyDecision { get; init; } = string.Empty;

    public string? PolicyReason { get; init; }

    public string Profile { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string WorkingDirectory { get; init; } = string.Empty;

    public int? ExitCode { get; init; }

    public bool TimedOut { get; init; }

    public string Stdout { get; init; } = string.Empty;

    public string Stderr { get; init; } = string.Empty;

    public bool StdoutTruncated { get; init; }

    public bool StderrTruncated { get; init; }

    public long ElapsedMilliseconds { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string TraceId { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset CompletedAt { get; init; }
}

public static class SshExecutionStatusNames
{
    public const string Denied = "denied";
    public const string Failed = "failed";
    public const string Succeeded = "succeeded";
    public const string TimedOut = "timed_out";
}
