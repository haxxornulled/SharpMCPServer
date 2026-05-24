using System.Collections.Concurrent;
using System.Text;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Tools.Ssh.Interfaces;
using MCPServer.Tools.Ssh.Models;
using Microsoft.Extensions.Logging;

namespace MCPServer.Tools.Ssh.Services;

public sealed class SshAgentRuntime : ISshAgentRuntime, IDisposable
{
    private const int DefaultPollIntervalMilliseconds = 1000;
    private const int TailChars = 8000;

    private readonly ISshExecutionService _executionService;
    private readonly ILogger<SshAgentRuntime> _logger;
    private readonly ConcurrentDictionary<string, SshAgentRun> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();

    public SshAgentRuntime(ISshExecutionService executionService, ILogger<SshAgentRuntime> logger)
    {
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<Fin<SshAgentLaunchResponse>> LaunchAsync(SshAgentLaunchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateLaunchRequest(request);
        if (validation is not null)
        {
            return new ValueTask<Fin<SshAgentLaunchResponse>>(Fin.Fail<SshAgentLaunchResponse>(Error.New(validation)));
        }

        var now = DateTimeOffset.UtcNow;
        var agentId = "ssh-agent-" + Guid.NewGuid().ToString("N");
        var state = new SshAgentState(
            agentId,
            request.Profile.Trim(),
            request.Objective.Trim(),
            request.WorkingDirectory?.Trim(),
            request.TimeoutSecondsPerStep,
            string.IsNullOrWhiteSpace(request.OperationKey) ? agentId : request.OperationKey.Trim(),
            request.Commands.Select(static command => new SshAgentCommandRequest
            {
                Command = command.Command.Trim(),
                Arguments = command.Arguments.ToArray(),
                WorkingDirectory = command.WorkingDirectory?.Trim(),
                TimeoutSeconds = command.TimeoutSeconds
            }).ToArray(),
            now);

        var run = new SshAgentRun(state, CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token));
        if (!_agents.TryAdd(agentId, run))
        {
            run.Dispose();
            return new ValueTask<Fin<SshAgentLaunchResponse>>(Fin.Fail<SshAgentLaunchResponse>(Error.New("Failed to register SSH agent.")));
        }

        _ = Task.Run(() => ExecuteAgentAsync(run), CancellationToken.None);
        return new ValueTask<Fin<SshAgentLaunchResponse>>(Fin.Succ<SshAgentLaunchResponse>(ToLaunchResponse(run.State)));
    }

    public ValueTask<Fin<SshAgentStatusResponse>> GetStatusAsync(string agentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(agentId))
        {
            return new ValueTask<Fin<SshAgentStatusResponse>>(Fin.Fail<SshAgentStatusResponse>(Error.New("agentId is required.")));
        }

        return new ValueTask<Fin<SshAgentStatusResponse>>(
            _agents.TryGetValue(agentId.Trim(), out var run)
                ? Fin.Succ<SshAgentStatusResponse>(ToStatusResponse(run.State))
                : Fin.Fail<SshAgentStatusResponse>(Error.New($"SSH agent '{agentId}' was not found.")));
    }

    public ValueTask<Fin<SshAgentOutputResponse>> GetOutputAsync(SshAgentOutputRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            return new ValueTask<Fin<SshAgentOutputResponse>>(Fin.Fail<SshAgentOutputResponse>(Error.New("agentId is required.")));
        }

        if (!_agents.TryGetValue(request.AgentId.Trim(), out var run))
        {
            return new ValueTask<Fin<SshAgentOutputResponse>>(Fin.Fail<SshAgentOutputResponse>(Error.New($"SSH agent '{request.AgentId}' was not found.")));
        }

        return new ValueTask<Fin<SshAgentOutputResponse>>(Fin.Succ<SshAgentOutputResponse>(ToOutputResponse(run.State, request)));
    }

    public ValueTask<Fin<SshAgentCancelResponse>> CancelAsync(string agentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(agentId))
        {
            return new ValueTask<Fin<SshAgentCancelResponse>>(Fin.Fail<SshAgentCancelResponse>(Error.New("agentId is required.")));
        }

        if (!_agents.TryGetValue(agentId.Trim(), out var run))
        {
            return new ValueTask<Fin<SshAgentCancelResponse>>(Fin.Fail<SshAgentCancelResponse>(Error.New($"SSH agent '{agentId}' was not found.")));
        }

        lock (run.State.SyncRoot)
        {
            if (!IsTerminal(run.State.Status))
            {
                run.State.CancellationRequested = true;
                run.State.Status = SshAgentStatusNames.Cancelled;
                run.State.Summary = "SSH agent cancellation was requested. If an SSH command is already running, the remote command may continue until SSH.NET returns or its timeout is reached.";
                run.State.LastUpdatedAt = DateTimeOffset.UtcNow;
                run.State.CompletedAt ??= run.State.LastUpdatedAt;
            }
        }

        run.Cancellation.Cancel();
        return new ValueTask<Fin<SshAgentCancelResponse>>(Fin.Succ<SshAgentCancelResponse>(ToCancelResponse(run.State)));
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        foreach (var run in _agents.Values)
        {
            run.Dispose();
        }

        _shutdown.Dispose();
    }

    private async Task ExecuteAgentAsync(SshAgentRun run)
    {
        try
        {
            lock (run.State.SyncRoot)
            {
                if (run.State.Status == SshAgentStatusNames.Cancelled)
                {
                    return;
                }

                run.State.Status = SshAgentStatusNames.Working;
                run.State.Summary = "SSH agent is running.";
                run.State.LastUpdatedAt = DateTimeOffset.UtcNow;
            }

            for (var index = 0; index < run.State.Commands.Count; index++)
            {
                if (run.Cancellation.IsCancellationRequested || run.State.CancellationRequested)
                {
                    MarkCancelled(run.State);
                    return;
                }

                var command = run.State.Commands[index];
                BeginStep(run.State, index, command);

                var request = new SshExecutionRequest
                {
                    Profile = run.State.Profile,
                    Command = command.Command,
                    Arguments = command.Arguments.ToArray(),
                    WorkingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? run.State.WorkingDirectory : command.WorkingDirectory,
                    TimeoutSeconds = command.TimeoutSeconds ?? run.State.TimeoutSecondsPerStep,
                    OperationKey = run.State.OperationKey
                };

                var execution = await _executionService.ExecuteAsync(request, run.Cancellation.Token).ConfigureAwait(false);
                if (execution.IsFail)
                {
                    var error = execution.Match(
                        Succ: _ => throw new InvalidOperationException("Unexpected SSH execution success while handling failure."),
                        Fail: failure => failure);
                    CompleteStep(run.State, index, SshAgentStatusNames.Failed, null, error.Message, string.Empty, error.Message);
                    MarkFailed(run.State, error.Message);
                    return;
                }

                var response = execution.Match(
                    Succ: value => value,
                    Fail: _ => throw new InvalidOperationException("Unexpected SSH execution failure while handling success."));

                var stepStatus = string.Equals(response.Status, SshExecutionStatusNames.Succeeded, StringComparison.Ordinal)
                    ? SshAgentStatusNames.Completed
                    : SshAgentStatusNames.Failed;

                CompleteStep(run.State, index, stepStatus, response.ExitCode, response.Summary, response.Stdout, response.Stderr);

                if (!string.Equals(stepStatus, SshAgentStatusNames.Completed, StringComparison.Ordinal))
                {
                    MarkFailed(run.State, response.Summary);
                    return;
                }
            }

            lock (run.State.SyncRoot)
            {
                if (run.State.Status != SshAgentStatusNames.Cancelled)
                {
                    run.State.Status = SshAgentStatusNames.Completed;
                    run.State.Summary = "SSH agent completed all commands successfully.";
                    run.State.CompletedAt = DateTimeOffset.UtcNow;
                    run.State.LastUpdatedAt = run.State.CompletedAt.Value;
                }
            }
        }
        catch (OperationCanceledException)
        {
            MarkCancelled(run.State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSH agent {AgentId} failed", run.State.AgentId);
            MarkFailed(run.State, ex.Message);
        }
    }

    private static string? ValidateLaunchRequest(SshAgentLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Profile))
        {
            return "profile is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Objective))
        {
            return "objective is required.";
        }

        if (request.Commands.Count == 0)
        {
            return "commands must include at least one command.";
        }

        if (request.Commands.Count > 100)
        {
            return "commands cannot include more than 100 entries.";
        }

        for (var i = 0; i < request.Commands.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(request.Commands[i].Command))
            {
                return $"commands[{i}] must include command.";
            }
        }

        return null;
    }

    private static void BeginStep(SshAgentState state, int index, SshAgentCommandRequest command)
    {
        lock (state.SyncRoot)
        {
            state.CurrentStep = index + 1;
            state.Status = SshAgentStatusNames.Working;
            state.Summary = $"Running step {index + 1} of {state.Commands.Count}: {command.Command}";
            state.Steps[index] = state.Steps[index] with
            {
                Status = SshAgentStatusNames.Working,
                StartedAt = DateTimeOffset.UtcNow,
                Summary = "Running."
            };
            state.LastUpdatedAt = state.Steps[index].StartedAt.GetValueOrDefault(state.LastUpdatedAt);
        }
    }

    private static void CompleteStep(SshAgentState state, int index, string status, int? exitCode, string summary, string stdout, string stderr)
    {
        lock (state.SyncRoot)
        {
            state.Stdout.Append(stdout);
            state.Stderr.Append(stderr);
            if (!string.IsNullOrEmpty(stdout) && !stdout.EndsWith('\n'))
            {
                state.Stdout.AppendLine();
            }
            if (!string.IsNullOrEmpty(stderr) && !stderr.EndsWith('\n'))
            {
                state.Stderr.AppendLine();
            }

            state.Steps[index] = state.Steps[index] with
            {
                Status = status,
                ExitCode = exitCode,
                Summary = summary,
                CompletedAt = DateTimeOffset.UtcNow
            };
            state.LastUpdatedAt = state.Steps[index].CompletedAt.GetValueOrDefault(DateTimeOffset.UtcNow);
            state.Summary = summary;
        }
    }

    private static void MarkCancelled(SshAgentState state)
    {
        lock (state.SyncRoot)
        {
            state.CancellationRequested = true;
            state.Status = SshAgentStatusNames.Cancelled;
            state.Summary = "SSH agent was cancelled.";
            state.CompletedAt ??= DateTimeOffset.UtcNow;
            state.LastUpdatedAt = state.CompletedAt.Value;
        }
    }

    private static void MarkFailed(SshAgentState state, string summary)
    {
        lock (state.SyncRoot)
        {
            if (state.Status == SshAgentStatusNames.Cancelled)
            {
                return;
            }

            state.Status = SshAgentStatusNames.Failed;
            state.Summary = string.IsNullOrWhiteSpace(summary) ? "SSH agent failed." : summary;
            state.CompletedAt = DateTimeOffset.UtcNow;
            state.LastUpdatedAt = state.CompletedAt.Value;
        }
    }

    private static SshAgentLaunchResponse ToLaunchResponse(SshAgentState state)
    {
        lock (state.SyncRoot)
        {
            return new SshAgentLaunchResponse
            {
                AgentId = state.AgentId,
                Status = state.Status,
                Profile = state.Profile,
                Objective = state.Objective,
                CommandCount = state.Commands.Count,
                CurrentStep = state.CurrentStep,
                PollIntervalMilliseconds = DefaultPollIntervalMilliseconds,
                CreatedAt = state.CreatedAt,
                LastUpdatedAt = state.LastUpdatedAt,
                Summary = state.Summary
            };
        }
    }

    private static SshAgentStatusResponse ToStatusResponse(SshAgentState state)
    {
        lock (state.SyncRoot)
        {
            return new SshAgentStatusResponse
            {
                AgentId = state.AgentId,
                Status = state.Status,
                Profile = state.Profile,
                Objective = state.Objective,
                CommandCount = state.Commands.Count,
                CurrentStep = state.CurrentStep,
                CompletedSteps = state.Steps.Count(static step => step.Status == SshAgentStatusNames.Completed),
                FailedSteps = state.Steps.Count(static step => step.Status == SshAgentStatusNames.Failed),
                CancellationRequested = state.CancellationRequested,
                CurrentCommand = state.CurrentStep > 0 && state.CurrentStep <= state.Steps.Length ? state.Steps[state.CurrentStep - 1].Command : string.Empty,
                Summary = state.Summary,
                StdoutTail = Tail(state.Stdout.ToString(), TailChars),
                StderrTail = Tail(state.Stderr.ToString(), TailChars),
                StdoutLength = state.Stdout.Length,
                StderrLength = state.Stderr.Length,
                Steps = state.Steps.Select(static step => new SshAgentStepSnapshot
                {
                    Index = step.Index,
                    Status = step.Status,
                    Command = step.Command,
                    Arguments = step.Arguments.ToArray(),
                    ExitCode = step.ExitCode,
                    Summary = step.Summary,
                    StartedAt = step.StartedAt,
                    CompletedAt = step.CompletedAt
                }).ToArray(),
                CreatedAt = state.CreatedAt,
                LastUpdatedAt = state.LastUpdatedAt,
                CompletedAt = state.CompletedAt
            };
        }
    }

    private static SshAgentOutputResponse ToOutputResponse(SshAgentState state, SshAgentOutputRequest request)
    {
        lock (state.SyncRoot)
        {
            var stdout = ReadWindow(state.Stdout.ToString(), request.StdoutOffset, request.MaxChars, out var nextStdoutOffset, out var stdoutTruncated);
            var stderr = ReadWindow(state.Stderr.ToString(), request.StderrOffset, request.MaxChars, out var nextStderrOffset, out var stderrTruncated);

            return new SshAgentOutputResponse
            {
                AgentId = state.AgentId,
                Status = state.Status,
                Stdout = stdout,
                Stderr = stderr,
                StdoutOffset = Math.Max(0, request.StdoutOffset),
                StderrOffset = Math.Max(0, request.StderrOffset),
                NextStdoutOffset = nextStdoutOffset,
                NextStderrOffset = nextStderrOffset,
                StdoutTruncated = stdoutTruncated,
                StderrTruncated = stderrTruncated
            };
        }
    }

    private static SshAgentCancelResponse ToCancelResponse(SshAgentState state)
    {
        lock (state.SyncRoot)
        {
            return new SshAgentCancelResponse
            {
                AgentId = state.AgentId,
                Status = state.Status,
                CancellationRequested = state.CancellationRequested,
                Summary = state.Summary,
                LastUpdatedAt = state.LastUpdatedAt
            };
        }
    }

    private static string ReadWindow(string value, int offset, int maxChars, out int nextOffset, out bool truncated)
    {
        var safeOffset = Math.Clamp(offset, 0, value.Length);
        var safeMax = Math.Clamp(maxChars, 1, 100000);
        var available = value.Length - safeOffset;
        var length = Math.Min(available, safeMax);
        truncated = available > safeMax;
        nextOffset = safeOffset + length;
        return length <= 0 ? string.Empty : value.Substring(safeOffset, length);
    }

    private static string Tail(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        return value[^maxChars..];
    }

    private static bool IsTerminal(string status)
    {
        return status is SshAgentStatusNames.Completed or SshAgentStatusNames.Failed or SshAgentStatusNames.Cancelled;
    }

    private sealed class SshAgentRun : IDisposable
    {
        public SshAgentRun(SshAgentState state, CancellationTokenSource cancellation)
        {
            State = state;
            Cancellation = cancellation;
        }

        public SshAgentState State { get; }

        public CancellationTokenSource Cancellation { get; }

        public void Dispose()
        {
            Cancellation.Dispose();
        }
    }

    private sealed class SshAgentState
    {
        public SshAgentState(
            string agentId,
            string profile,
            string objective,
            string? workingDirectory,
            int? timeoutSecondsPerStep,
            string operationKey,
            IReadOnlyList<SshAgentCommandRequest> commands,
            DateTimeOffset createdAt)
        {
            AgentId = agentId;
            Profile = profile;
            Objective = objective;
            WorkingDirectory = workingDirectory;
            TimeoutSecondsPerStep = timeoutSecondsPerStep;
            OperationKey = operationKey;
            Commands = commands;
            CreatedAt = createdAt;
            LastUpdatedAt = createdAt;
            Steps = commands.Select((command, index) => new SshAgentMutableStep
            {
                Index = index + 1,
                Status = SshAgentStatusNames.Queued,
                Command = command.Command,
                Arguments = command.Arguments.ToArray(),
                Summary = "Queued."
            }).ToArray();
            Summary = "SSH agent queued.";
        }

        public object SyncRoot { get; } = new();

        public string AgentId { get; }

        public string Profile { get; }

        public string Objective { get; }

        public string? WorkingDirectory { get; }

        public int? TimeoutSecondsPerStep { get; }

        public string OperationKey { get; }

        public IReadOnlyList<SshAgentCommandRequest> Commands { get; }

        public DateTimeOffset CreatedAt { get; }

        public DateTimeOffset LastUpdatedAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        public string Status { get; set; } = SshAgentStatusNames.Queued;

        public int CurrentStep { get; set; }

        public bool CancellationRequested { get; set; }

        public string Summary { get; set; }

        public SshAgentMutableStep[] Steps { get; }

        public StringBuilder Stdout { get; } = new();

        public StringBuilder Stderr { get; } = new();
    }

    private sealed record SshAgentMutableStep
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
}
