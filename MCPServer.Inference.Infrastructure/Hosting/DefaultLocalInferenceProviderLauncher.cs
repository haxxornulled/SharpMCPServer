using System.Diagnostics;
using System.Globalization;
using MCPServer.Inference.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace MCPServer.Inference.Infrastructure.Hosting;

public sealed class DefaultLocalInferenceProviderLauncher : ILocalInferenceProviderLauncher
{
    private readonly ILogger<DefaultLocalInferenceProviderLauncher> _logger;

    public DefaultLocalInferenceProviderLauncher(ILogger<DefaultLocalInferenceProviderLauncher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<ILocalInferenceProviderHandle?> StartAsync(
        string providerId,
        McpInferenceProviderOptions providerOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerOptions);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(providerId, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            return StartOllamaAsync(providerOptions, cancellationToken);
        }

        if (string.Equals(providerId, "lmstudio", StringComparison.OrdinalIgnoreCase))
        {
            return new ValueTask<ILocalInferenceProviderHandle?>(StartLmStudioAsync(providerOptions, cancellationToken));
        }

        return ValueTask.FromResult<ILocalInferenceProviderHandle?>(null);
    }

    private ValueTask<ILocalInferenceProviderHandle?> StartOllamaAsync(
        McpInferenceProviderOptions providerOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executablePath = LocalInferenceProviderProcessStartInfoFactory.ResolveOllamaExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            _logger.LogWarning("Unable to locate an Ollama executable on the current machine.");
            return ValueTask.FromResult<ILocalInferenceProviderHandle?>(null);
        }

        if (!Uri.TryCreate(providerOptions.BaseAddress, UriKind.Absolute, out var baseAddress))
        {
            _logger.LogWarning("Ollama base address '{BaseAddress}' is not a valid absolute URI.", providerOptions.BaseAddress);
            return ValueTask.FromResult<ILocalInferenceProviderHandle?>(null);
        }

        if (!LocalInferenceProviderProcessStartInfoFactory.TryCreateOllamaStartInfo(
                executablePath,
                baseAddress,
                providerOptions,
                out var startInfo))
        {
            return ValueTask.FromResult<ILocalInferenceProviderHandle?>(null);
        }

        var process = StartProcess(startInfo, "ollama serve");
        if (process is null)
        {
            return ValueTask.FromResult<ILocalInferenceProviderHandle?>(null);
        }

        AttachProcessLogging(process, "ollama");

        _logger.LogInformation(
            "Started Ollama provider bootstrap process from {ExecutablePath} with host {Host}.",
            executablePath,
            startInfo.Environment["OLLAMA_HOST"]);

        return ValueTask.FromResult<ILocalInferenceProviderHandle?>(new ProcessBackedLocalInferenceProviderHandle(
            providerId: "ollama",
            killProcess: process,
            stopAction: null,
            logger: _logger));
    }

    private async Task<ILocalInferenceProviderHandle?> StartLmStudioAsync(
        McpInferenceProviderOptions providerOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executablePath = LocalInferenceProviderProcessStartInfoFactory.ResolveLmStudioExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            _logger.LogWarning("Unable to locate an LM Studio lms executable on the current machine.");
            return null;
        }

        if (!Uri.TryCreate(providerOptions.BaseAddress, UriKind.Absolute, out var baseAddress))
        {
            _logger.LogWarning("LM Studio base address '{BaseAddress}' is not a valid absolute URI.", providerOptions.BaseAddress);
            return null;
        }

        var resolvedModel = await ResolveLmStudioModelAsync(executablePath, providerOptions, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(resolvedModel))
        {
            _logger.LogWarning(
                "Unable to determine an installed LM Studio model for bootstrap. The configured model '{Model}' is not usable and no fallback model was found.",
                providerOptions.Model);
            return null;
        }

        if (!string.Equals(providerOptions.Model.Trim(), resolvedModel, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "LM Studio bootstrap resolved configured model '{ConfiguredModel}' to installed model '{ResolvedModel}'.",
                providerOptions.Model,
                resolvedModel);
        }

        providerOptions.Model = resolvedModel;

        if (!LocalInferenceProviderProcessStartInfoFactory.TryCreateLmStudioServerStartInfo(
                executablePath,
                baseAddress,
                out var serverStartInfo))
        {
            return null;
        }

        var serverProcess = StartProcess(serverStartInfo, "lms server start");
        if (serverProcess is null)
        {
            return null;
        }

        AttachProcessLogging(serverProcess, "lmstudio-server");

        Process? loadProcess = null;
        if (LocalInferenceProviderProcessStartInfoFactory.TryCreateLmStudioLoadStartInfo(
                executablePath,
                providerOptions,
                out var loadStartInfo) &&
            loadStartInfo is not null)
        {
            loadProcess = StartProcess(loadStartInfo, "lms load");
            if (loadProcess is not null)
            {
                AttachProcessLogging(loadProcess, "lmstudio-load");
            }
        }

        _logger.LogInformation(
            "Started LM Studio provider bootstrap process from {ExecutablePath} on port {Port}.",
            executablePath,
            baseAddress.Port > 0 ? baseAddress.Port : 1234);

        return new ProcessBackedLocalInferenceProviderHandle(
            providerId: "lmstudio",
            killProcess: serverProcess,
            stopAction: async cancellationToken =>
            {
                if (loadProcess is not null)
                {
                    await TryTerminateProcessAsync(loadProcess, cancellationToken).ConfigureAwait(false);
                }

                if (!LocalInferenceProviderProcessStartInfoFactory.TryCreateLmStudioStopStartInfo(executablePath, out var stopStartInfo))
                {
                    return;
                }

                var stopProcess = StartProcess(stopStartInfo, "lms server stop");
                if (stopProcess is null)
                {
                    return;
                }

                AttachProcessLogging(stopProcess, "lmstudio-stop");
                try
                {
                    await stopProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    stopProcess.Dispose();
                }
            },
            logger: _logger);
    }

    private async Task<string?> ResolveLmStudioModelAsync(
        string executablePath,
        McpInferenceProviderOptions providerOptions,
        CancellationToken cancellationToken)
    {
        if (!LocalInferenceProviderProcessStartInfoFactory.TryCreateLmStudioListModelsStartInfo(
                executablePath,
                out var listModelsStartInfo))
        {
            return string.IsNullOrWhiteSpace(providerOptions.Model)
                ? null
                : providerOptions.Model.Trim();
        }

        using var discoveryTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryTimeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        ProcessExecutionResult commandResult;
        try
        {
            commandResult = await ExecuteCommandAsync(listModelsStartInfo, discoveryTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("LM Studio model discovery timed out after 10 seconds.");
            return string.IsNullOrWhiteSpace(providerOptions.Model)
                ? null
                : providerOptions.Model.Trim();
        }

        if (commandResult.ExitCode != 0)
        {
            _logger.LogDebug(
                "LM Studio model discovery exited with code {ExitCode}. stderr: {StandardError}",
                commandResult.ExitCode,
                commandResult.StandardError);
            return string.IsNullOrWhiteSpace(providerOptions.Model)
                ? null
                : providerOptions.Model.Trim();
        }

        var models = LocalInferenceModelDiscovery.ParseLmStudioModels(commandResult.StandardOutput);
        var selectedModel = LocalInferenceModelDiscovery.SelectPreferredModel(models, providerOptions.Model);
        if (!string.IsNullOrWhiteSpace(selectedModel))
        {
            return selectedModel;
        }

        return string.IsNullOrWhiteSpace(providerOptions.Model)
            ? null
            : providerOptions.Model.Trim();
    }

    private async Task<ProcessExecutionResult> ExecuteCommandAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            return new ProcessExecutionResult(-1, string.Empty, "Failed to start process.");
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort cancellation only.
            }
        });

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);
        return new ProcessExecutionResult(process.ExitCode, standardOutput, standardError);
    }

    private Process? StartProcess(ProcessStartInfo startInfo, string description)
    {
        try
        {
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                _logger.LogWarning("Failed to start provider bootstrap process for {Description}.", description);
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to start provider bootstrap process for {Description}.",
                description);
            return null;
        }
    }

    private sealed record ProcessExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private void AttachProcessLogging(Process process, string providerLabel)
    {
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                _logger.LogDebug("[{ProviderLabel}] {Line}", providerLabel, eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                _logger.LogWarning("[{ProviderLabel}] {Line}", providerLabel, eventArgs.Data);
            }
        };

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (InvalidOperationException)
        {
            // Some commands can exit before asynchronous reads attach; ignore and rely on
            // the process handle/stop path instead of failing provider startup.
        }
    }

    private static async ValueTask TryTerminateProcessAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best effort shutdown only.
        }
    }

    private sealed class ProcessBackedLocalInferenceProviderHandle : ILocalInferenceProviderHandle
    {
        private readonly string _providerId;
        private readonly Process? _killProcess;
        private readonly Func<CancellationToken, ValueTask>? _stopAction;
        private readonly ILogger _logger;
        private int _stopped;

        public ProcessBackedLocalInferenceProviderHandle(
            string providerId,
            Process? killProcess,
            Func<CancellationToken, ValueTask>? stopAction,
            ILogger logger)
        {
            _providerId = providerId;
            _killProcess = killProcess;
            _stopAction = stopAction;
            _logger = logger;
        }

        public async ValueTask StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            if (_stopAction is not null)
            {
                try
                {
                    await _stopAction(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "The stop action for provider bootstrap process {ProviderId} failed; continuing with process cleanup.",
                        _providerId);
                }
            }

            if (_killProcess is not null)
            {
                try
                {
                    if (!_killProcess.HasExited)
                    {
                        _killProcess.Kill(entireProcessTree: true);
                        await _killProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "Failed to terminate provider bootstrap process for {ProviderId}.",
                        _providerId);
                }
                finally
                {
                    _killProcess.Dispose();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
