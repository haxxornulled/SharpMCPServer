using System.Text.Json;
using MCPServer.Ssh.Configuration;

namespace MCPServer.Ssh.Models;

public static class SshJson
{
    public static JsonElement ToJsonElement(SshExecutionResponse response)
    {
        return Build(writer => WriteExecutionResponse(writer, response));
    }

    public static JsonElement ToProfilesJsonElement(
        SshProfileCatalog catalog,
        IReadOnlyDictionary<string, bool>? credentialAvailability = null)
    {
        return Build(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartArray("profiles");
            foreach (var profile in catalog.Profiles.Values.OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteStartObject();
                writer.WriteString("name", profile.Name);
                writer.WriteString("displayName", string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Name : profile.DisplayName);
                writer.WriteString("host", profile.Host);
                writer.WriteNumber("port", profile.Port <= 0 ? 22 : profile.Port);
                writer.WriteString("username", profile.Username);
                writer.WriteString("credentialKind", GetCredentialKind(profile));
                writer.WriteBoolean("hasCredentialConfigured", HasCredentialReference(profile));
                writer.WriteBoolean("passwordEnvironmentVariableSet", IsPasswordCredentialAvailable(profile, credentialAvailability));
                writer.WriteBoolean("hostKeyPinned", !string.IsNullOrWhiteSpace(profile.HostKeySha256));
                writer.WriteBoolean("acceptUnknownHostKey", profile.AcceptUnknownHostKey);
                writer.WriteBoolean("allowAllCommands", profile.AllowAllCommands);
                writer.WriteBoolean("privileged", profile.Privileged);
                writer.WriteBoolean("allowedRoot", profile.AllowedRoot);
                WriteStringArray(writer, "allowedCommands", profile.AllowedCommands);
                WriteStringArray(writer, "allowedRemotePathPrefixes", profile.AllowedRemotePathPrefixes);
                writer.WriteString("source", profile.Source);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("sources");
            foreach (var source in catalog.Sources)
            {
                writer.WriteStartObject();
                writer.WriteString("path", source.Path);
                writer.WriteBoolean("exists", source.Exists);
                writer.WriteNumber("profileCount", source.ProfileCount);
                if (!string.IsNullOrWhiteSpace(source.Error))
                {
                    writer.WriteString("error", source.Error);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }


    public static JsonElement ToAgentLaunchJsonElement(SshAgentLaunchResponse response)
    {
        return Build(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("agentId", response.AgentId);
            writer.WriteString("status", response.Status);
            writer.WriteString("profile", response.Profile);
            writer.WriteString("objective", response.Objective);
            writer.WriteNumber("commandCount", response.CommandCount);
            writer.WriteNumber("currentStep", response.CurrentStep);
            writer.WriteNumber("pollIntervalMilliseconds", response.PollIntervalMilliseconds);
            writer.WriteString("createdAt", response.CreatedAt);
            writer.WriteString("lastUpdatedAt", response.LastUpdatedAt);
            writer.WriteString("summary", response.Summary);
            writer.WriteEndObject();
        });
    }

    public static JsonElement ToAgentStatusJsonElement(SshAgentStatusResponse response)
    {
        return Build(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("agentId", response.AgentId);
            writer.WriteString("status", response.Status);
            writer.WriteString("profile", response.Profile);
            writer.WriteString("objective", response.Objective);
            writer.WriteNumber("commandCount", response.CommandCount);
            writer.WriteNumber("currentStep", response.CurrentStep);
            writer.WriteNumber("completedSteps", response.CompletedSteps);
            writer.WriteNumber("failedSteps", response.FailedSteps);
            writer.WriteBoolean("cancellationRequested", response.CancellationRequested);
            writer.WriteString("currentCommand", response.CurrentCommand);
            writer.WriteString("summary", response.Summary);
            writer.WriteString("stdoutTail", response.StdoutTail);
            writer.WriteString("stderrTail", response.StderrTail);
            writer.WriteNumber("stdoutLength", response.StdoutLength);
            writer.WriteNumber("stderrLength", response.StderrLength);
            writer.WriteStartArray("steps");
            foreach (var step in response.Steps)
            {
                WriteAgentStep(writer, step);
            }
            writer.WriteEndArray();
            writer.WriteString("createdAt", response.CreatedAt);
            writer.WriteString("lastUpdatedAt", response.LastUpdatedAt);
            if (response.CompletedAt is { } completedAt)
            {
                writer.WriteString("completedAt", completedAt);
            }
            else
            {
                writer.WriteNull("completedAt");
            }
            writer.WriteEndObject();
        });
    }

    public static JsonElement ToAgentOutputJsonElement(SshAgentOutputResponse response)
    {
        return Build(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("agentId", response.AgentId);
            writer.WriteString("status", response.Status);
            writer.WriteString("stdout", response.Stdout);
            writer.WriteString("stderr", response.Stderr);
            writer.WriteNumber("stdoutOffset", response.StdoutOffset);
            writer.WriteNumber("stderrOffset", response.StderrOffset);
            writer.WriteNumber("nextStdoutOffset", response.NextStdoutOffset);
            writer.WriteNumber("nextStderrOffset", response.NextStderrOffset);
            writer.WriteBoolean("stdoutTruncated", response.StdoutTruncated);
            writer.WriteBoolean("stderrTruncated", response.StderrTruncated);
            writer.WriteEndObject();
        });
    }

    public static JsonElement ToAgentCancelJsonElement(SshAgentCancelResponse response)
    {
        return Build(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("agentId", response.AgentId);
            writer.WriteString("status", response.Status);
            writer.WriteBoolean("cancellationRequested", response.CancellationRequested);
            writer.WriteString("summary", response.Summary);
            writer.WriteString("lastUpdatedAt", response.LastUpdatedAt);
            writer.WriteEndObject();
        });
    }

    public static string ToTraceJson(SshExecutionResponse response)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            WriteExecutionResponse(writer, response);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static JsonElement Build(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static string GetCredentialKind(SshProfileDefinition profile)
    {
        var hasPrivateKey = !string.IsNullOrWhiteSpace(profile.PrivateKeyPath);
        var hasPasswordEnvironmentVariable = !string.IsNullOrWhiteSpace(profile.PasswordEnvironmentVariable);

        return (hasPrivateKey, hasPasswordEnvironmentVariable) switch
        {
            (true, true) => "multiple",
            (true, false) => "private-key",
            (false, true) => "password-environment-variable",
            _ => "none"
        };
    }

    private static bool HasCredentialReference(SshProfileDefinition profile)
    {
        return !string.IsNullOrWhiteSpace(profile.PrivateKeyPath) ||
            !string.IsNullOrWhiteSpace(profile.PasswordEnvironmentVariable);
    }

    private static bool IsPasswordCredentialAvailable(
        SshProfileDefinition profile,
        IReadOnlyDictionary<string, bool>? credentialAvailability)
    {
        return profile.PasswordEnvironmentVariable is { Length: > 0 } credentialReference &&
            credentialAvailability?.TryGetValue(credentialReference, out var available) == true &&
            available;
    }

    private static void WriteExecutionResponse(Utf8JsonWriter writer, SshExecutionResponse response)
    {
        writer.WriteStartObject();
        writer.WriteString("id", response.Id);
        writer.WriteString("status", response.Status);
        writer.WriteBoolean("allowed", response.Allowed);
        writer.WriteString("policyDecision", response.PolicyDecision);
        if (!string.IsNullOrWhiteSpace(response.PolicyReason))
        {
            writer.WriteString("policyReason", response.PolicyReason);
        }
        writer.WriteString("profile", response.Profile);
        writer.WriteString("command", response.Command);
        WriteStringArray(writer, "arguments", response.Arguments);
        writer.WriteString("workingDirectory", response.WorkingDirectory);
        if (response.ExitCode is { } exitCode)
        {
            writer.WriteNumber("exitCode", exitCode);
        }
        else
        {
            writer.WriteNull("exitCode");
        }
        writer.WriteBoolean("timedOut", response.TimedOut);
        writer.WriteString("stdout", response.Stdout);
        writer.WriteString("stderr", response.Stderr);
        writer.WriteBoolean("stdoutTruncated", response.StdoutTruncated);
        writer.WriteBoolean("stderrTruncated", response.StderrTruncated);
        writer.WriteNumber("elapsedMilliseconds", response.ElapsedMilliseconds);
        writer.WriteString("summary", response.Summary);
        writer.WriteString("traceId", response.TraceId);
        writer.WriteString("createdAt", response.CreatedAt);
        writer.WriteString("completedAt", response.CompletedAt);
        writer.WriteEndObject();
    }


    private static void WriteAgentStep(Utf8JsonWriter writer, SshAgentStepSnapshot step)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", step.Index);
        writer.WriteString("status", step.Status);
        writer.WriteString("command", step.Command);
        WriteStringArray(writer, "arguments", step.Arguments);
        if (step.ExitCode is { } exitCode)
        {
            writer.WriteNumber("exitCode", exitCode);
        }
        else
        {
            writer.WriteNull("exitCode");
        }
        writer.WriteString("summary", step.Summary);
        if (step.StartedAt is { } startedAt)
        {
            writer.WriteString("startedAt", startedAt);
        }
        else
        {
            writer.WriteNull("startedAt");
        }
        if (step.CompletedAt is { } completedAt)
        {
            writer.WriteString("completedAt", completedAt);
        }
        else
        {
            writer.WriteNull("completedAt");
        }
        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string propertyName, IEnumerable<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }
}
