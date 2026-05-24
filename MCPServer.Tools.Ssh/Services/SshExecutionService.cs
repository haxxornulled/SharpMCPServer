using LanguageExt;
using MCPServer.Tools.Ssh.Interfaces;
using MCPServer.Tools.Ssh.Models;
using Microsoft.Extensions.Logging;

namespace MCPServer.Tools.Ssh.Services;

public sealed class SshExecutionService : ISshExecutionService
{
    private readonly ISshExecutionPolicy _policy;
    private readonly ISshCommandExecutor _executor;
    private readonly ISshExecutionTraceWriter _traceWriter;
    private readonly ILogger<SshExecutionService> _logger;

    public SshExecutionService(
        ISshExecutionPolicy policy,
        ISshCommandExecutor executor,
        ISshExecutionTraceWriter traceWriter,
        ILogger<SshExecutionService> logger)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _traceWriter = traceWriter ?? throw new ArgumentNullException(nameof(traceWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<SshExecutionResponse>> ExecuteAsync(SshExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var traceId = "ssh-" + Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;

        var policyResult = await _policy.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (policyResult.IsFail)
        {
            return policyResult.Match<Fin<SshExecutionResponse>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH policy success while handling failure."),
                Fail: error => error);
        }

        var policy = policyResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH policy failure while handling success."));

        if (!policy.Allowed)
        {
            var denied = new SshExecutionResponse
            {
                Id = traceId,
                Status = SshExecutionStatusNames.Denied,
                Allowed = false,
                PolicyDecision = policy.Decision,
                PolicyReason = policy.Reason,
                Profile = request.Profile.Trim(),
                Command = request.Command.Trim(),
                Arguments = request.Arguments.ToArray(),
                WorkingDirectory = request.WorkingDirectory?.Trim() ?? string.Empty,
                Summary = policy.Reason ?? "SSH execution was denied by policy.",
                TraceId = traceId,
                CreatedAt = createdAt,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await WriteTraceBestEffortAsync(denied, cancellationToken).ConfigureAwait(false);
            return Fin.Succ<SshExecutionResponse>(denied);
        }

        var executionCommand = new SshExecutionCommand
        {
            ProfileName = policy.ProfileName,
            Host = policy.Host,
            Port = policy.Port,
            Username = policy.Username,
            Command = policy.ResolvedCommand,
            Arguments = policy.ResolvedArguments.ToArray(),
            WorkingDirectory = policy.WorkingDirectory,
            TimeoutSeconds = policy.TimeoutSeconds,
            MaxOutputChars = policy.MaxOutputChars,
            PrivateKeyPath = policy.PrivateKeyPath,
            PrivateKeyPassphraseEnvironmentVariable = policy.PrivateKeyPassphraseEnvironmentVariable,
            PasswordEnvironmentVariable = policy.PasswordEnvironmentVariable,
            HostKeySha256 = policy.HostKeySha256,
            AcceptUnknownHostKey = policy.AcceptUnknownHostKey,
            OperationKey = request.OperationKey
        };

        var execution = await _executor.ExecuteAsync(executionCommand, cancellationToken).ConfigureAwait(false);
        if (execution.IsFail)
        {
            var error = execution.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH execution success while handling failure."),
                Fail: failure => failure);

            var failed = new SshExecutionResponse
            {
                Id = traceId,
                Status = SshExecutionStatusNames.Failed,
                Allowed = true,
                PolicyDecision = policy.Decision,
                Profile = policy.ProfileName,
                Command = policy.ResolvedCommand,
                Arguments = policy.ResolvedArguments.ToArray(),
                WorkingDirectory = policy.WorkingDirectory,
                Summary = error.Message,
                Stderr = error.Message,
                TraceId = traceId,
                CreatedAt = createdAt,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await WriteTraceBestEffortAsync(failed, cancellationToken).ConfigureAwait(false);
            return Fin.Succ<SshExecutionResponse>(failed);
        }

        var result = execution.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH execution failure while handling success."));

        var status = result.TimedOut
            ? SshExecutionStatusNames.TimedOut
            : result.ExitCode == 0 ? SshExecutionStatusNames.Succeeded : SshExecutionStatusNames.Failed;

        var response = new SshExecutionResponse
        {
            Id = traceId,
            Status = status,
            Allowed = true,
            PolicyDecision = policy.Decision,
            Profile = policy.ProfileName,
            Command = policy.ResolvedCommand,
            Arguments = policy.ResolvedArguments.ToArray(),
            WorkingDirectory = policy.WorkingDirectory,
            ExitCode = result.ExitCode,
            TimedOut = result.TimedOut,
            Stdout = result.Stdout,
            Stderr = result.Stderr,
            StdoutTruncated = result.StdoutTruncated,
            StderrTruncated = result.StderrTruncated,
            ElapsedMilliseconds = result.ElapsedMilliseconds,
            Summary = BuildSummary(policy.ResolvedCommand, result),
            TraceId = traceId,
            CreatedAt = createdAt,
            CompletedAt = DateTimeOffset.UtcNow
        };

        await WriteTraceBestEffortAsync(response, cancellationToken).ConfigureAwait(false);
        return Fin.Succ<SshExecutionResponse>(response);
    }

    private async ValueTask WriteTraceBestEffortAsync(SshExecutionResponse response, CancellationToken cancellationToken)
    {
        var written = await _traceWriter.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        if (written.IsFail)
        {
            var error = written.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH trace write success while handling failure."),
                Fail: failure => failure);
            _logger.LogWarning("Failed to write SSH execution trace {TraceId}: {Message}", response.TraceId, error.Message);
        }
    }

    private static string BuildSummary(string command, SshCommandExecutionResult result)
    {
        if (result.TimedOut)
        {
            return $"SSH command '{command}' timed out after {result.ElapsedMilliseconds}ms.";
        }

        return $"SSH command '{command}' exited with code {result.ExitCode} after {result.ElapsedMilliseconds}ms.";
    }
}
