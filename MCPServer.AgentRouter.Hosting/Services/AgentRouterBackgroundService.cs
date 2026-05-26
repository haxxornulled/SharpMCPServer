using LanguageExt;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MCPServer.AgentRouter.Application.Interfaces;

namespace MCPServer.AgentRouter.Hosting.Services;

public sealed class AgentRouterBackgroundService : BackgroundService, IHostedLifecycleService
{
    private readonly IAgentRouterWorker _worker;
    private readonly IReadOnlyList<IAgentRouterStartupTask> _startupTasks;
    private readonly AgentRouterBackgroundServiceOptions _options;
    private readonly ILogger<AgentRouterBackgroundService> _logger;

    public AgentRouterBackgroundService(
        IAgentRouterWorker worker,
        IEnumerable<IAgentRouterStartupTask> startupTasks,
        AgentRouterBackgroundServiceOptions options,
        ILogger<AgentRouterBackgroundService> logger)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _startupTasks = (startupTasks ?? throw new ArgumentNullException(nameof(startupTasks))).ToArray();
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.IdleDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Idle delay cannot be negative.");
        }
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AgentRouter hosted lifecycle service is starting.");

        foreach (var startupTask in _startupTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(startupTask.Name))
            {
                throw new InvalidOperationException("AgentRouter startup task name is required.");
            }

            _logger.LogInformation(
                "AgentRouter startup task '{StartupTaskName}' is running.",
                startupTask.Name);

            var result = await startupTask.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsFail)
            {
                var error = result.Match(
                    Succ: static _ => throw new InvalidOperationException("Unexpected startup task success while handling failure."),
                    Fail: static error => error);

                throw new InvalidOperationException(
                    $"AgentRouter startup task '{startupTask.Name}' failed. {error.Message}");
            }

            _logger.LogInformation(
                "AgentRouter startup task '{StartupTaskName}' completed.",
                startupTask.Name);
        }
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AgentRouter hosted lifecycle service started.");
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AgentRouter hosted lifecycle service is stopping.");
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AgentRouter hosted lifecycle service stopped.");
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RunImmediately && _options.IdleDelay > TimeSpan.Zero)
        {
            await DelayAsync(_options.IdleDelay, stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleSafelyAsync(stoppingToken).ConfigureAwait(false);

            if (_options.IdleDelay > TimeSpan.Zero)
            {
                await DelayAsync(_options.IdleDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask RunCycleSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = await _worker.RunCycleAsync(stoppingToken).ConfigureAwait(false);

            result.Match(
                Succ: cycleResult =>
                {
                    if (cycleResult.ProcessedCount > 0)
                    {
                        _logger.LogInformation(
                            "AgentRouter background service processed {ProcessedCount} item(s). {Message}",
                            cycleResult.ProcessedCount,
                            cycleResult.Message);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "AgentRouter background service completed an idle cycle. {Message}",
                            cycleResult.Message);
                    }

                    return default(Unit);
                },
                Fail: error =>
                {
                    _logger.LogWarning(
                        "AgentRouter background service cycle failed: {ErrorMessage}",
                        error.Message);

                    return default(Unit);
                });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown path.
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "AgentRouter background service cycle threw an unhandled exception.");
        }
    }

    private static async ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown path.
        }
    }
}
