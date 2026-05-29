using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure.Mcp.Http;
using Xunit;

namespace MCPServer.UnitTests.Client.Console;

public sealed class McpClientConsoleSmokeTests
{
    [Fact]
    public async Task Console_Can_Smoke_Test_A_Real_Streamable_Http_Host()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var port = GetAvailablePort();
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        using var hostProcess = StartProcess(
            "dotnet",
            [
                hostDll,
                "--McpTransport:Http:Enabled=true",
                $"--McpTransport:Http:Port={port}",
                "--McpTransport:Http:BindLoopbackOnly=true",
                "--McpTransport:Http:SseHeartbeatMilliseconds=100",
                "--McpTransport:Http:MaxSessionHistoryMessages=8",
                "--McpInference:Providers:lmstudio:Enabled=false",
                "--McpInference:Providers:ollama:Enabled=false"
            ],
            hostWorkingDirectory);

        try
        {
            await WaitForHttpTransportAsync(port, cancellationToken);

            var consoleResult = await RunProcessAsync(
                "dotnet",
                [
                    consoleDll,
                    "--endpoint",
                    $"http://127.0.0.1:{port}/mcp/",
                    "--open-server-event-stream",
                    "--tool",
                    "server.info",
                    "--arguments",
                    "{}"
                ],
                consoleWorkingDirectory,
                cancellationToken);

            Assert.Equal(0, consoleResult.ExitCode);
            Assert.Contains("Connected to", consoleResult.Stdout, StringComparison.Ordinal);
            Assert.Contains("Tools exposed by server:", consoleResult.Stdout, StringComparison.Ordinal);
            Assert.Contains("Calling tool: server.info", consoleResult.Stdout, StringComparison.Ordinal);
            Assert.Contains("Tool returned a success result.", consoleResult.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            await TryKillProcessAsync(hostProcess, cancellationToken);
        }
    }

    [Fact]
    public async Task Console_Can_Smoke_Test_A_Real_Stdio_Host()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        var consoleResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--working-directory",
                hostWorkingDirectory,
                "--tool",
                "server.info",
                "--arguments",
                "{}"
            ],
            consoleWorkingDirectory,
            cancellationToken);

        Assert.Equal(0, consoleResult.ExitCode);
        Assert.Contains("Connected to", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tools exposed by server:", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Calling tool: server.info", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tool returned a success result.", consoleResult.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_Can_Demo_Client_Sampling_Over_Stdio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        var consoleResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--working-directory",
                hostWorkingDirectory,
                "--demo-sampling",
                "--tool",
                "client.sample",
                "--arguments",
                "{\"prompt\":\"Say hello in one sentence.\"}"
            ],
            consoleWorkingDirectory,
            cancellationToken);

        Assert.Equal(0, consoleResult.ExitCode);
        Assert.Contains("Calling tool: client.sample", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Demo assistant reply:", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tool returned a success result.", consoleResult.Stdout, StringComparison.Ordinal);
    }

    [Fact(SkipExceptions = new[] { typeof(LiveInferenceUnavailableException) })]
    public async Task Console_Can_Select_Inference_Provider_With_Shortcut_Over_Stdio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        var providerSelectionResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--working-directory",
                hostWorkingDirectory,
                "--tool",
                "inference.providers.list",
                "--probe",
                "--probe-timeout-ms",
                "3000"
            ],
            consoleWorkingDirectory,
            cancellationToken);

        Assert.Equal(0, providerSelectionResult.ExitCode);
        Assert.Contains("Calling tool: inference.providers.list", providerSelectionResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tool returned a success result.", providerSelectionResult.Stdout, StringComparison.Ordinal);

        var liveTargets = await DiscoverLiveInferenceTargetsAsync(providerSelectionResult.Stdout, hostWorkingDirectory, cancellationToken);
        if (liveTargets.Count == 0)
        {
            throw new LiveInferenceUnavailableException("No live inference providers with installed models were detected over stdio.");
        }

        foreach (var target in liveTargets)
        {
            var consoleResult = await RunProcessAsync(
                "dotnet",
                [
                    consoleDll,
                    "--transport",
                    "stdio",
                    "--server-path",
                    "dotnet",
                    "--server-arg",
                    hostDll,
                    "--working-directory",
                    hostWorkingDirectory,
                    "--tool",
                    "inference.generate",
                    "--arguments",
                    "{\"prompt\":\"Say hello in one sentence.\",\"strategy\":\"PrimaryOnly\"}",
                    "--provider",
                    target.ProviderId,
                    "--model",
                    target.Model
                ],
                consoleWorkingDirectory,
                cancellationToken);

            Assert.Equal(0, consoleResult.ExitCode);
            Assert.Contains("Calling tool: inference.generate", consoleResult.Stdout, StringComparison.Ordinal);
            Assert.Contains("Tool returned a success result.", consoleResult.Stdout, StringComparison.Ordinal);
            AssertInferenceResponseMetadata(consoleResult.Stdout, target.ProviderId, target.Model);
        }
    }

    [Fact]
    public async Task Console_Can_Prompt_To_Start_Inference_When_The_Configured_Provider_Binding_Is_Unreachable_Over_Stdio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var providerPort = GetAvailablePort();
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        var consoleResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--server-arg",
                "--McpInference:Providers:lmstudio:Enabled=true",
                "--server-arg",
                $"--McpInference:Providers:lmstudio:BaseAddress=http://127.0.0.1:{providerPort}/v1/",
                "--server-arg",
                "--McpInference:Providers:lmstudio:Model=local-model",
                "--server-arg",
                "--McpInference:Providers:lmstudio:HttpClientName=lmstudio",
                "--working-directory",
                hostWorkingDirectory,
                "--tool",
                "inference.generate",
                "--arguments",
                "{\"prompt\":\"Say hello in one sentence.\",\"strategy\":\"PrimaryOnly\"}",
                "--provider",
                "lmstudio",
                "--model",
                "local-model"
            ],
            consoleWorkingDirectory,
            cancellationToken);

        Assert.Equal(0, consoleResult.ExitCode);
        Assert.Contains("Calling tool: inference.generate", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tool returned an error result.", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("All inference providers failed:", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Hint: start the configured provider process, then retry.", consoleResult.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_Can_Prompt_To_Start_Inference_And_Continue_The_Chat_Loop_When_The_Configured_Provider_Binding_Is_Unreachable_Over_Stdio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var providerPort = GetAvailablePort();
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        var consoleResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--server-arg",
                "--McpInference:Providers:lmstudio:Enabled=true",
                "--server-arg",
                $"--McpInference:Providers:lmstudio:BaseAddress=http://127.0.0.1:{providerPort}/v1/",
                "--server-arg",
                "--McpInference:Providers:lmstudio:Model=local-model",
                "--server-arg",
                "--McpInference:Providers:lmstudio:HttpClientName=lmstudio",
                "--working-directory",
                hostWorkingDirectory,
                "--chat",
                "--provider",
                "lmstudio",
                "--model",
                "local-model"
            ],
            consoleWorkingDirectory,
            cancellationToken,
            [
                "/strategy PrimaryOnly",
                "hello",
                "/exit"
            ]);

        Assert.Equal(0, consoleResult.ExitCode);
        Assert.Contains("Chat mode ready.", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Routing strategy set to PrimaryOnly.", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("assistant> [tool returned an error]", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("All inference providers failed:", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Hint: start the configured provider process, then retry.", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Equal(3, CountPromptLines(consoleResult.Stdout));
    }

    [Fact(SkipExceptions = new[] { typeof(LiveInferenceUnavailableException) })]
    public async Task Console_Can_Switch_Between_Live_Inference_Providers_Within_Chat_Over_Stdio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        var providerSelectionResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--working-directory",
                hostWorkingDirectory,
                "--tool",
                "inference.providers.list",
                "--probe",
                "--probe-timeout-ms",
                "3000"
            ],
            consoleWorkingDirectory,
            cancellationToken);

        Assert.Equal(0, providerSelectionResult.ExitCode);
        Assert.Contains("Calling tool: inference.providers.list", providerSelectionResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tool returned a success result.", providerSelectionResult.Stdout, StringComparison.Ordinal);

        var liveTargets = await DiscoverLiveInferenceTargetsAsync(providerSelectionResult.Stdout, hostWorkingDirectory, cancellationToken);
        if (liveTargets.Count < 2)
        {
            throw new LiveInferenceUnavailableException("At least two live inference providers with installed models are required to exercise provider switching over stdio.");
        }

        var initialTarget = liveTargets[0];
        var switchedTarget = liveTargets[1];

        var consoleResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--working-directory",
                hostWorkingDirectory,
                "--chat",
                "--provider",
                initialTarget.ProviderId,
                "--model",
                initialTarget.Model
            ],
            consoleWorkingDirectory,
            cancellationToken,
            [
                "/strategy PrimaryOnly",
                "hello",
                $"/provider {switchedTarget.ProviderId}",
                $"/model {switchedTarget.Model}",
                "hello again",
                "/exit"
            ]);

        Assert.Equal(0, consoleResult.ExitCode);
        Assert.Contains("Chat mode ready.", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Routing strategy set to PrimaryOnly.", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("assistant>", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("assistant> [tool returned an error]", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains($"provider set to {switchedTarget.ProviderId}.", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"model set to {switchedTarget.Model}.", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"provider={initialTarget.ProviderId}", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"provider={switchedTarget.ProviderId}", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"model={initialTarget.Model}", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"model={switchedTarget.Model}", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, CountPromptLines(consoleResult.Stdout));
    }

    [Fact(SkipExceptions = new[] { typeof(LiveInferenceUnavailableException) })]
    public async Task Console_Can_Select_Inference_Providers_And_Generate_Over_Http()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");
        var hostPort = GetAvailablePort();

        var liveTargets = await DiscoverLiveProviderTargetsAsync(cancellationToken);
        if (liveTargets.Count == 0)
        {
            throw new LiveInferenceUnavailableException("No live inference providers with installed models were detected for HTTP packet capture.");
        }

        var liveTargetsByProviderId = liveTargets.ToDictionary(target => target.ProviderId, StringComparer.OrdinalIgnoreCase);
        var providerProxies = new Dictionary<string, HttpCaptureProxy>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var target in liveTargets)
            {
                providerProxies[target.ProviderId] = await HttpCaptureProxy.StartAsync(target.UpstreamBaseUri, cancellationToken);
            }

            var providerProxyBaseUris = providerProxies.ToDictionary(pair => pair.Key, pair => pair.Value.BaseUri, StringComparer.OrdinalIgnoreCase);
            var hostArguments = new List<string>
            {
                hostDll,
                "--McpTransport:Http:Enabled=true",
                $"--McpTransport:Http:Port={hostPort}",
                "--McpTransport:Http:BindLoopbackOnly=true",
                "--McpTransport:Http:SseHeartbeatMilliseconds=100",
                "--McpTransport:Http:MaxSessionHistoryMessages=8"
            };
            hostArguments.AddRange(CreateLiveHttpInferenceOverrides(liveTargetsByProviderId, providerProxyBaseUris));

            using var hostProcess = StartProcess(
                "dotnet",
                hostArguments,
                hostWorkingDirectory);

            try
            {
                await WaitForHttpTransportAsync(hostPort, cancellationToken);

                await using var hostProxy = await HttpCaptureProxy.StartAsync(new Uri($"http://127.0.0.1:{hostPort}/", UriKind.Absolute), cancellationToken);
                var hostEndpoint = new Uri(hostProxy.BaseUri, "mcp/").AbsoluteUri;

                var providerSelectionResult = await RunProcessAsync(
                    "dotnet",
                    [
                        consoleDll,
                        "--endpoint",
                        hostEndpoint,
                        "--open-server-event-stream",
                        "--tool",
                        "inference.providers.list",
                        "--probe",
                        "--probe-timeout-ms",
                        "3000"
                    ],
                    consoleWorkingDirectory,
                    cancellationToken);

                Assert.Equal(0, providerSelectionResult.ExitCode);
                Assert.Contains("Calling tool: inference.providers.list", providerSelectionResult.Stdout, StringComparison.Ordinal);
                Assert.Contains("Tool returned a success result.", providerSelectionResult.Stdout, StringComparison.Ordinal);

                var readyProviders = TrySelectReadyInferenceProviders(providerSelectionResult.Stdout);
                Assert.Equal(liveTargets.Count, readyProviders.Count);
                foreach (var target in liveTargets)
                {
                    Assert.Contains(target.ProviderId, readyProviders, StringComparer.OrdinalIgnoreCase);
                    AssertProviderProbeResult(
                        providerSelectionResult.Stdout,
                        target.ProviderId,
                        "ready",
                        new Uri(providerProxyBaseUris[target.ProviderId], GetProviderProbePath(target.ProviderId)).AbsoluteUri,
                        200);

                    var providerProbeExchange = providerProxies[target.ProviderId].Exchanges
                        .Where(exchange =>
                            string.Equals(exchange.Method, HttpMethod.Get.Method, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(exchange.PathAndQuery, GetProviderProbePath(target.ProviderId), StringComparison.Ordinal))
                        .ToArray();
                    Assert.NotEmpty(providerProbeExchange);

                    AssertProviderProbeExchange(providerProbeExchange[0], target.ProviderId, target.Model);
                }

                AssertHostProviderListExchange(hostProxy.Exchanges, readyProviders);

                var selectedTarget = liveTargets[0];
                var hostExchangeStart = hostProxy.Exchanges.Count;
                var providerExchangeStart = providerProxies[selectedTarget.ProviderId].Exchanges.Count;

                var consoleResult = await RunProcessAsync(
                    "dotnet",
                    [
                        consoleDll,
                        "--endpoint",
                        hostEndpoint,
                        "--tool",
                        "inference.generate",
                        "--arguments",
                        "{\"prompt\":\"Say hello in one sentence.\",\"strategy\":\"PrimaryOnly\"}",
                        "--provider",
                        selectedTarget.ProviderId,
                        "--model",
                        selectedTarget.Model
                    ],
                    consoleWorkingDirectory,
                    cancellationToken);

                AssertSuccessfulProcessResult(consoleResult);
                Assert.Contains("Connected to", consoleResult.Stdout, StringComparison.Ordinal);
                Assert.Contains("Calling tool: inference.generate", consoleResult.Stdout, StringComparison.Ordinal);
                Assert.Contains("Tool returned a success result.", consoleResult.Stdout, StringComparison.Ordinal);
                AssertInferenceResponseMetadata(consoleResult.Stdout, selectedTarget.ProviderId, selectedTarget.Model);

                var hostExchanges = hostProxy.Exchanges.Skip(hostExchangeStart).ToArray();
                AssertHostGenerateExchange(hostExchanges, selectedTarget.ProviderId, selectedTarget.Model);

                var providerExchanges = providerProxies[selectedTarget.ProviderId].Exchanges.Skip(providerExchangeStart).ToArray();
                AssertProviderGenerateExchange(providerExchanges, selectedTarget.ProviderId, selectedTarget.Model);
            }
            finally
            {
                await TryKillProcessAsync(hostProcess, cancellationToken);
            }
        }
        finally
        {
            foreach (var proxy in providerProxies.Values)
            {
                await proxy.DisposeAsync();
            }
        }
    }

    [Fact(SkipExceptions = new[] { typeof(LiveInferenceUnavailableException) })]
    public async Task Console_Can_Switch_Between_Live_Inference_Providers_Within_Chat_Over_Http()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");
        var hostPort = GetAvailablePort();

        var liveTargets = await DiscoverLiveProviderTargetsAsync(cancellationToken);
        if (liveTargets.Count < 2)
        {
            throw new LiveInferenceUnavailableException("At least two live inference providers with installed models are required to exercise provider switching over HTTP packet capture.");
        }

        var liveTargetsByProviderId = liveTargets.ToDictionary(target => target.ProviderId, StringComparer.OrdinalIgnoreCase);
        var providerProxies = new Dictionary<string, HttpCaptureProxy>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var target in liveTargets)
            {
                providerProxies[target.ProviderId] = await HttpCaptureProxy.StartAsync(target.UpstreamBaseUri, cancellationToken);
            }

            var providerProxyBaseUris = providerProxies.ToDictionary(pair => pair.Key, pair => pair.Value.BaseUri, StringComparer.OrdinalIgnoreCase);
            var hostArguments = new List<string>
            {
                hostDll,
                "--McpTransport:Http:Enabled=true",
                $"--McpTransport:Http:Port={hostPort}",
                "--McpTransport:Http:BindLoopbackOnly=true",
                "--McpTransport:Http:SseHeartbeatMilliseconds=100",
                "--McpTransport:Http:MaxSessionHistoryMessages=8"
            };
            hostArguments.AddRange(CreateLiveHttpInferenceOverrides(liveTargetsByProviderId, providerProxyBaseUris));

            using var hostProcess = StartProcess(
                "dotnet",
                hostArguments,
                hostWorkingDirectory);

            try
            {
                await WaitForHttpTransportAsync(hostPort, cancellationToken);

                await using var hostProxy = await HttpCaptureProxy.StartAsync(new Uri($"http://127.0.0.1:{hostPort}/", UriKind.Absolute), cancellationToken);
                var hostEndpoint = new Uri(hostProxy.BaseUri, "mcp/").AbsoluteUri;

                var initialTarget = liveTargets[0];
                var switchedTarget = liveTargets[1];

                var consoleResult = await RunProcessAsync(
                    "dotnet",
                    [
                        consoleDll,
                        "--endpoint",
                        hostEndpoint,
                        "--open-server-event-stream",
                        "--chat",
                        "--provider",
                        initialTarget.ProviderId,
                        "--model",
                        initialTarget.Model
                    ],
                    consoleWorkingDirectory,
                    cancellationToken,
                    [
                        "/strategy PrimaryOnly",
                        "hello",
                        $"/provider {switchedTarget.ProviderId}",
                        $"/model {switchedTarget.Model}",
                        "hello again",
                        "/exit"
                    ]);

                Assert.Equal(0, consoleResult.ExitCode);
                Assert.Contains("Chat mode ready.", consoleResult.Stdout, StringComparison.Ordinal);
                Assert.Contains("Routing strategy set to PrimaryOnly.", consoleResult.Stdout, StringComparison.Ordinal);
                Assert.Contains("assistant>", consoleResult.Stdout, StringComparison.Ordinal);
                Assert.DoesNotContain("assistant> [tool returned an error]", consoleResult.Stdout, StringComparison.Ordinal);
                Assert.Contains($"provider set to {switchedTarget.ProviderId}.", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"model set to {switchedTarget.Model}.", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"provider={initialTarget.ProviderId}", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"provider={switchedTarget.ProviderId}", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"model={initialTarget.Model}", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"model={switchedTarget.Model}", consoleResult.Stdout, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(6, CountPromptLines(consoleResult.Stdout));

                AssertHostChatSessionExchanges(hostProxy.Exchanges, initialTarget, switchedTarget);
                AssertProviderChatExchanges(providerProxies[initialTarget.ProviderId].Exchanges.ToArray(), initialTarget, "hello");
                AssertProviderChatExchanges(providerProxies[switchedTarget.ProviderId].Exchanges.ToArray(), switchedTarget, "hello again");
            }
            finally
            {
                await TryKillProcessAsync(hostProcess, cancellationToken);
            }
        }
        finally
        {
            foreach (var proxy in providerProxies.Values)
            {
                await proxy.DisposeAsync();
            }
        }
    }

    private static async Task WaitForHttpTransportAsync(int port, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var endpoint = new Uri($"http://127.0.0.1:{port}/mcp/", UriKind.Absolute);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        var origin = endpoint.GetLeftPart(UriPartial.Authority);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await SendInitializeProbeAsync(httpClient, endpoint, origin, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    if (response.Headers.TryGetValues(StreamableHttpMcpHeaderNames.SessionId, out var sessionIds))
                    {
                        var sessionId = sessionIds.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(sessionId))
                        {
                            await SendDeleteProbeAsync(httpClient, endpoint, origin, sessionId, cancellationToken);
                        }
                    }

                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for the Streamable HTTP transport to start.");
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> SendInitializeProbeAsync(HttpClient httpClient, Uri endpoint, string origin, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Mcp-Method", McpMethods.Initialize);
        request.Content = new StringContent(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"smoke-test","version":"1"}}}
            """,
            Encoding.UTF8,
            "application/json");

        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task SendDeleteProbeAsync(HttpClient httpClient, Uri endpoint, string origin, string sessionId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", McpProtocolVersions.Current);
        request.Headers.TryAddWithoutValidation("MCP-Session-Id", sessionId);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"Expected HTTP 204 from DELETE probe, but received {(int)response.StatusCode}.");
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? standardInputLines = null)
    {
        using var process = StartProcess(fileName, arguments, workingDirectory);
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (standardInputLines is not null)
            {
                foreach (var line in standardInputLines)
                {
                    await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
                }
            }

            process.StandardInput.Close();

            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        finally
        {
            await TryKillProcessAsync(process, CancellationToken.None);
        }
    }

    private static void AssertSuccessfulProcessResult(ProcessResult result)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        Assert.Fail(
            $"Expected exit code 0 but got {result.ExitCode}.\nSTDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}");
    }

    private static Process StartProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
    }

    private static async Task TryKillProcessAsync(Process process, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
        }
    }

    private static int CountPromptLines(string stdout)
    {
        return stdout
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.StartsWith("> [", StringComparison.Ordinal));
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GetProjectOutputPath(string projectName, string fileName, string configuration)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var path = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", projectName, "bin", configuration, "net10.0", fileName));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find built artifact '{fileName}' for project '{projectName}'.", path);
        }

        return path;
    }

    private static string? TrySelectReadyInferenceProvider(string stdout)
    {
        return TrySelectReadyInferenceProviders(stdout).FirstOrDefault();
    }

    private static IReadOnlyList<string> TrySelectReadyInferenceProviders(string stdout)
    {
        var readyProviders = new List<string>();

        if (!TryGetStructuredContent(stdout, out var structuredContent))
        {
            return readyProviders;
        }

        if (!structuredContent.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array)
        {
            return readyProviders;
        }

        foreach (var provider in providers.EnumerateArray())
        {
            if (!provider.TryGetProperty("status", out var statusProperty) || statusProperty.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!string.Equals(statusProperty.GetString(), "ready", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!provider.TryGetProperty("providerId", out var providerIdProperty) || providerIdProperty.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var providerId = providerIdProperty.GetString();
            if (!string.IsNullOrWhiteSpace(providerId))
            {
                readyProviders.Add(providerId);
            }
        }

        return readyProviders;
    }

    private static async Task<string?> TryResolveInstalledInferenceModelAsync(string hostWorkingDirectory, string providerId, CancellationToken cancellationToken)
    {
        var appSettingsPath = Path.Combine(hostWorkingDirectory, "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            return null;
        }

        using var appSettingsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(appSettingsPath, cancellationToken).ConfigureAwait(false));
        if (!TryGetString(appSettingsDocument.RootElement, out var baseAddress, "McpInference", "Providers", providerId, "BaseAddress"))
        {
            return null;
        }

        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var providerBaseUri))
        {
            return null;
        }

        return await TryResolveInstalledInferenceModelAsync(providerBaseUri, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<LiveInferenceTarget>> DiscoverLiveInferenceTargetsAsync(
        string stdout,
        string hostWorkingDirectory,
        CancellationToken cancellationToken)
    {
        var liveTargets = new List<LiveInferenceTarget>();
        foreach (var providerId in TrySelectReadyInferenceProviders(stdout))
        {
            var model = await TryResolveInstalledInferenceModelAsync(hostWorkingDirectory, providerId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(model))
            {
                liveTargets.Add(new LiveInferenceTarget(providerId, model));
            }
        }

        return liveTargets;
    }

    private static async Task<IReadOnlyList<LiveProviderTarget>> DiscoverLiveProviderTargetsAsync(CancellationToken cancellationToken)
    {
        var liveTargets = new List<LiveProviderTarget>();
        foreach (var (providerId, providerBaseUri) in
            new (string ProviderId, Uri ProviderBaseUri)[]
            {
                ("lmstudio", GetLiveLmStudioBaseUri()),
                ("ollama", GetLiveOllamaBaseUri())
            })
        {
            var model = await TryResolveInstalledInferenceModelAsync(providerBaseUri, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(model))
            {
                liveTargets.Add(new LiveProviderTarget(providerId, providerBaseUri, model));
            }
        }

        return liveTargets;
    }

    private static string? SelectPreferredInferenceModelId(IReadOnlyList<string> modelIds)
    {
        var candidates = modelIds
            .Select((modelId, discoveryOrder) => new InferenceModelCandidate(
                ModelId: modelId,
                DiscoveryOrder: discoveryOrder,
                SizeInBillions: TryParseModelSizeInBillions(modelId, out var size) ? size : null))
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.ModelId) &&
                !candidate.ModelId.Contains("embed", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var scoredCandidates = candidates
            .Where(candidate => candidate.SizeInBillions.HasValue)
            .OrderBy(candidate => candidate.SizeInBillions!.Value)
            .ThenBy(candidate => candidate.ModelId.Length)
            .ThenBy(candidate => candidate.DiscoveryOrder)
            .ToArray();

        if (scoredCandidates.Length > 0)
        {
            return scoredCandidates[0].ModelId;
        }

        return candidates
            .OrderBy(candidate => candidate.DiscoveryOrder)
            .First()
            .ModelId;
    }

    private static bool TryParseModelSizeInBillions(string modelId, out double sizeInBillions)
    {
        var match = Regex.Match(
            modelId,
            @"(?<!\d)(?<size>\d+(?:\.\d+)?)b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (match.Success &&
            double.TryParse(match.Groups["size"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out sizeInBillions))
        {
            return true;
        }

        sizeInBillions = default;
        return false;
    }

    private static Uri GetLiveLmStudioBaseUri()
    {
        var configuredBaseAddress = Environment.GetEnvironmentVariable("MCP_LMSTUDIO_BASE_ADDRESS")
            ?? Environment.GetEnvironmentVariable("LMSTUDIO_BASE_ADDRESS")
            ?? "http://127.0.0.1:1234/v1/";

        if (!Uri.TryCreate(configuredBaseAddress, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"The LM Studio base address '{configuredBaseAddress}' is not a valid absolute URI.");
        }

        var normalizedBaseAddress = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri.AbsoluteUri
            : baseUri.AbsoluteUri + "/";

        return new Uri(normalizedBaseAddress, UriKind.Absolute);
    }

    private static Uri GetLiveOllamaBaseUri()
    {
        var configuredBaseAddress = Environment.GetEnvironmentVariable("MCP_OLLAMA_BASE_ADDRESS")
            ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_ADDRESS")
            ?? "http://127.0.0.1:11434/v1/";

        if (!Uri.TryCreate(configuredBaseAddress, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"The Ollama base address '{configuredBaseAddress}' is not a valid absolute URI.");
        }

        var normalizedBaseAddress = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri.AbsoluteUri
            : baseUri.AbsoluteUri + "/";

        return new Uri(normalizedBaseAddress, UriKind.Absolute);
    }

    private static IReadOnlyList<string> CreateLiveHttpInferenceOverrides(
        IReadOnlyDictionary<string, LiveProviderTarget> liveTargetsByProviderId,
        IReadOnlyDictionary<string, Uri> providerProxyBaseUris)
    {
        var arguments = new List<string>();
        foreach (var providerId in new[] { "lmstudio", "ollama" })
        {
            if (!liveTargetsByProviderId.TryGetValue(providerId, out var liveTarget) ||
                !providerProxyBaseUris.TryGetValue(providerId, out var providerProxyBaseUri))
            {
                arguments.Add($"--McpInference:Providers:{providerId}:Enabled=False");
                continue;
            }

            arguments.Add($"--McpInference:Providers:{providerId}:Enabled=True");
            arguments.Add($"--McpInference:Providers:{providerId}:BaseAddress={new Uri(providerProxyBaseUri, "v1/").AbsoluteUri}");
            arguments.Add($"--McpInference:Providers:{providerId}:Model={liveTarget.Model}");
            arguments.Add($"--McpInference:Providers:{providerId}:HttpClientName={providerId}");
        }

        return arguments;
    }

    private static async Task<string> RequireLiveLmStudioModelAsync(Uri providerBaseUri, CancellationToken cancellationToken)
    {
        var model = await TryResolveInstalledInferenceModelAsync(providerBaseUri, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(model))
        {
            return model;
        }

        throw new LiveLmStudioUnavailableException($"LM Studio at '{providerBaseUri}' did not report any installed models.");
    }

    private static async Task<string?> TryResolveInstalledInferenceModelAsync(Uri providerBaseUri, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        try
        {
            using var response = await httpClient.GetAsync(new Uri(providerBaseUri, "models"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!responseDocument.RootElement.TryGetProperty("data"u8, out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var modelIds = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (TryGetString(item, out var modelId, "id") && !string.IsNullOrWhiteSpace(modelId))
                {
                    modelIds.Add(modelId);
                }
            }

            return SelectPreferredInferenceModelId(modelIds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> CreateLiveLmStudioHostOverrides(Uri providerBaseUri, string model)
    {
        return
        [
            "--server-arg",
            "--McpInference:Providers:lmstudio:Enabled=true",
            "--server-arg",
            $"--McpInference:Providers:lmstudio:BaseAddress={providerBaseUri.AbsoluteUri}",
            "--server-arg",
            $"--McpInference:Providers:lmstudio:Model={model}",
            "--server-arg",
            "--McpInference:Providers:lmstudio:HttpClientName=lmstudio"
        ];
    }

    private static void AssertProviderProbeExchange(CapturedHttpExchange exchange, string providerId, string expectedModel)
    {
        var expectedPath = GetProviderProbePath(providerId);
        Assert.Equal(HttpMethod.Get.Method, exchange.Method, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expectedPath, exchange.PathAndQuery, StringComparer.Ordinal);
        Assert.Equal((int)HttpStatusCode.OK, exchange.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(exchange.ResponseBody));

        using var responseDocument = JsonDocument.Parse(exchange.ResponseBody);
        var root = responseDocument.RootElement;
        var expectedModelsArrayProperty = GetProviderProbeModelsArrayProperty(providerId);
        var expectedModelProperty = GetProviderProbeModelProperty(providerId);
        Assert.True(
            root.TryGetProperty(expectedModelsArrayProperty, out var models) && models.ValueKind == JsonValueKind.Array,
            $"Expected the provider probe response to contain a {expectedModelsArrayProperty} array.");
        Assert.True(models.GetArrayLength() > 0, "Expected the provider probe response to contain at least one model.");
        Assert.Contains(models.EnumerateArray(), model =>
            string.Equals(model.GetProperty(expectedModelProperty).GetString(), expectedModel, StringComparison.Ordinal));
    }

    private static string GetProviderProbePath(string providerId)
    {
        return string.Equals(providerId, "ollama", StringComparison.OrdinalIgnoreCase)
            ? "/api/tags"
            : "/v1/models";
    }

    private static string GetProviderProbeModelsArrayProperty(string providerId)
    {
        return string.Equals(providerId, "ollama", StringComparison.OrdinalIgnoreCase)
            ? "models"
            : "data";
    }

    private static string GetProviderProbeModelProperty(string providerId)
    {
        return string.Equals(providerId, "ollama", StringComparison.OrdinalIgnoreCase)
            ? "model"
            : "id";
    }

    private static void AssertProviderGenerateExchange(CapturedHttpExchange[] exchanges, string providerId, string model)
    {
        var expectedPath = GetProviderChatCompletionPath(providerId);
        var exchange = Assert.Single(exchanges, captured =>
            string.Equals(captured.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(captured.PathAndQuery, expectedPath, StringComparison.Ordinal));

        AssertProviderChatCompletionRequest(exchange, providerId, model, "Say hello in one sentence.");
        AssertProviderChatCompletionResponse(exchange, providerId, model);
    }

    private static void AssertProviderChatExchanges(CapturedHttpExchange[] exchanges, LiveProviderTarget target, string expectedPrompt)
    {
        var expectedPath = GetProviderChatCompletionPath(target.ProviderId);
        var exchange = Assert.Single(exchanges, captured =>
            string.Equals(captured.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(captured.PathAndQuery, expectedPath, StringComparison.Ordinal));

        AssertProviderChatCompletionRequest(exchange, target.ProviderId, target.Model, expectedPrompt);
        AssertProviderChatCompletionResponse(exchange, target.ProviderId, target.Model);
    }

    private static void AssertProviderChatCompletionRequest(CapturedHttpExchange exchange, string providerId, string model, string expectedPrompt)
    {
        Assert.Equal((int)HttpStatusCode.OK, exchange.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(exchange.RequestBody));

        using var requestDocument = JsonDocument.Parse(exchange.RequestBody);
        var root = requestDocument.RootElement;
        Assert.Equal(model, root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var messages = root.GetProperty("messages");
        Assert.True(messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0, "Expected at least one chat message.");
        Assert.Contains(messages.EnumerateArray(), message =>
            message.TryGetProperty("role", out var role) &&
            string.Equals(role.GetString(), "user", StringComparison.OrdinalIgnoreCase) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String &&
            (content.GetString() ?? string.Empty).Contains(expectedPrompt, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertProviderChatCompletionResponse(CapturedHttpExchange exchange, string providerId, string model)
    {
        Assert.Equal((int)HttpStatusCode.OK, exchange.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(exchange.ResponseBody));

        using var responseDocument = JsonDocument.Parse(exchange.ResponseBody);
        var root = responseDocument.RootElement;

        if (string.Equals(providerId, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            if (root.TryGetProperty("model"u8, out var responseModel) && responseModel.ValueKind == JsonValueKind.String)
            {
                Assert.Equal(model, responseModel.GetString(), StringComparer.Ordinal);
            }

            Assert.True(root.TryGetProperty("message"u8, out var ollamaMessage) && ollamaMessage.ValueKind == JsonValueKind.Object, $"Expected provider '{providerId}' to return a message object.");
            Assert.False(string.IsNullOrWhiteSpace(ollamaMessage.GetProperty("content").GetString()));
            return;
        }

        if (root.TryGetProperty("model"u8, out var openAiResponseModel) && openAiResponseModel.ValueKind == JsonValueKind.String)
        {
            Assert.Equal(model, openAiResponseModel.GetString(), StringComparer.Ordinal);
        }

        Assert.True(root.TryGetProperty("choices"u8, out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0, $"Expected provider '{providerId}' to return a non-empty choices array.");
        var choiceMessage = choices[0].GetProperty("message");
        Assert.False(string.IsNullOrWhiteSpace(choiceMessage.GetProperty("content").GetString()));
    }

    private static string GetProviderChatCompletionPath(string providerId)
    {
        return string.Equals(providerId, "ollama", StringComparison.OrdinalIgnoreCase)
            ? "/api/chat"
            : "/v1/chat/completions";
    }

    private static void AssertHostProviderListExchange(IReadOnlyList<CapturedHttpExchange> exchanges, IReadOnlyList<string> readyProviders)
    {
        var toolsListExchange = Assert.Single(exchanges, exchange =>
            string.Equals(exchange.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(exchange.GetRequestHeader("Mcp-Method"), McpMethods.ToolsList, StringComparison.Ordinal));
        AssertMcpRequestHeaders(toolsListExchange, expectedMethod: McpMethods.ToolsList, expectedProtocolVersion: McpProtocolVersions.Current);
        AssertMcpSessionHeader(toolsListExchange);

        var toolCall = FindSingleToolCallExchange(exchanges, "inference.providers.list");
        AssertMcpRequestHeaders(toolCall, expectedMethod: McpMethods.ToolsCall, expectedProtocolVersion: McpProtocolVersions.Current);
        AssertMcpSessionHeader(toolCall);

        using var requestDocument = JsonDocument.Parse(toolCall.RequestBody);
        var requestRoot = requestDocument.RootElement;
        var parameters = requestRoot.GetProperty("params");
        Assert.Equal("inference.providers.list", parameters.GetProperty("name").GetString());
        var arguments = parameters.GetProperty("arguments");
        Assert.True(arguments.GetProperty("probe").GetBoolean());
        Assert.Equal(3000, arguments.GetProperty("probeTimeoutMilliseconds").GetInt32());

        using var responseDocument = JsonDocument.Parse(toolCall.ResponseBody);
        var responseRoot = responseDocument.RootElement;
        var result = responseRoot.GetProperty("result");
        Assert.True(result.TryGetProperty("structuredContent"u8, out var structuredContent));
        Assert.True(structuredContent.TryGetProperty("providers"u8, out var providers) && providers.ValueKind == JsonValueKind.Array, "Expected providers structured content in the inference.providers.list response.");
        Assert.True(TryGetToolsArray(toolsListExchange.ResponseBody, out var tools), "Expected the tools/list response to contain a tools array.");
        Assert.Contains(tools.EnumerateArray(), tool =>
        {
            var name = tool.GetProperty("name").GetString() ?? string.Empty;
            return name.Contains("generate", StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(tools.EnumerateArray(), tool =>
        {
            var name = tool.GetProperty("name").GetString() ?? string.Empty;
            return name.Contains("providers", StringComparison.OrdinalIgnoreCase) &&
                   name.Contains("list", StringComparison.OrdinalIgnoreCase);
        });

        foreach (var readyProvider in readyProviders)
        {
            Assert.Contains(providers.EnumerateArray(), provider =>
                string.Equals(provider.GetProperty("providerId").GetString(), readyProvider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(provider.GetProperty("status").GetString(), "ready", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void AssertHostGenerateExchange(IReadOnlyList<CapturedHttpExchange> exchanges, string providerId, string model)
    {
        var toolsListExchange = Assert.Single(exchanges, exchange =>
            string.Equals(exchange.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(exchange.GetRequestHeader("Mcp-Method"), McpMethods.ToolsList, StringComparison.Ordinal));
        AssertMcpRequestHeaders(toolsListExchange, expectedMethod: McpMethods.ToolsList, expectedProtocolVersion: McpProtocolVersions.Current);
        AssertMcpSessionHeader(toolsListExchange);
        Assert.True(TryGetToolsArray(toolsListExchange.ResponseBody, out var tools), "Expected the tools/list response to contain a tools array.");
        Assert.Contains(tools.EnumerateArray(), tool =>
        {
            var name = tool.GetProperty("name").GetString() ?? string.Empty;
            return name.Contains("generate", StringComparison.OrdinalIgnoreCase);
        });

        var toolCall = FindSingleToolCallExchange(exchanges, "inference.generate");
        AssertMcpRequestHeaders(toolCall, expectedMethod: McpMethods.ToolsCall, expectedProtocolVersion: McpProtocolVersions.Current);
        AssertMcpSessionHeader(toolCall);

        using var requestDocument = JsonDocument.Parse(toolCall.RequestBody);
        var requestRoot = requestDocument.RootElement;
        var parameters = requestRoot.GetProperty("params");
        Assert.Equal("inference.generate", parameters.GetProperty("name").GetString());
        var arguments = parameters.GetProperty("arguments");
        Assert.Equal(providerId, arguments.GetProperty("providerId").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(model, arguments.GetProperty("model").GetString(), StringComparer.Ordinal);
        Assert.Equal("Say hello in one sentence.", arguments.GetProperty("prompt").GetString(), StringComparer.Ordinal);
        Assert.Equal("PrimaryOnly", arguments.GetProperty("strategy").GetString(), StringComparer.Ordinal);

        using var responseDocument = JsonDocument.Parse(toolCall.ResponseBody);
        var responseRoot = responseDocument.RootElement;
        var result = responseRoot.GetProperty("result");
        var structuredContent = result.GetProperty("structuredContent");
        Assert.Equal(providerId, structuredContent.GetProperty("providerId").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(model, structuredContent.GetProperty("model").GetString(), StringComparer.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(structuredContent.GetProperty("content").GetString()));
    }

    private static void AssertHostChatSessionExchanges(IReadOnlyList<CapturedHttpExchange> exchanges, LiveProviderTarget initialTarget, LiveProviderTarget switchedTarget)
    {
        var initializeExchange = Assert.Single(exchanges, exchange =>
            string.Equals(exchange.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(exchange.GetRequestHeader("Mcp-Method"), McpMethods.Initialize, StringComparison.Ordinal));
        AssertMcpRequestHeaders(initializeExchange, expectedMethod: McpMethods.Initialize, expectedProtocolVersion: null);

        using var initializeDocument = JsonDocument.Parse(initializeExchange.RequestBody);
        Assert.Equal(McpMethods.Initialize, initializeDocument.RootElement.GetProperty("method").GetString());

        var initializedExchange = Assert.Single(exchanges, exchange =>
            string.Equals(exchange.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(exchange.GetRequestHeader("Mcp-Method"), McpMethods.NotificationsInitialized, StringComparison.Ordinal));
        AssertMcpRequestHeaders(initializedExchange, expectedMethod: McpMethods.NotificationsInitialized, expectedProtocolVersion: McpProtocolVersions.Current);
        AssertMcpSessionHeader(initializedExchange);

        var getExchange = Assert.Single(exchanges, exchange => string.Equals(exchange.Method, HttpMethod.Get.Method, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("/mcp/", getExchange.PathAndQuery, StringComparer.Ordinal);
        Assert.StartsWith("text/event-stream", getExchange.GetResponseHeader("Content-Type") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        AssertMcpSessionHeader(getExchange);

        var toolsListExchange = Assert.Single(exchanges, exchange =>
            string.Equals(exchange.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(exchange.GetRequestHeader("Mcp-Method"), McpMethods.ToolsList, StringComparison.Ordinal));
        AssertMcpRequestHeaders(toolsListExchange, expectedMethod: McpMethods.ToolsList, expectedProtocolVersion: McpProtocolVersions.Current);
        AssertMcpSessionHeader(toolsListExchange);
        Assert.True(TryGetToolsArray(toolsListExchange.ResponseBody, out var tools), "Expected the tools/list response to contain a tools array.");
        Assert.Contains(tools.EnumerateArray(), tool =>
        {
            var name = tool.GetProperty("name").GetString() ?? string.Empty;
            return name.Contains("generate", StringComparison.OrdinalIgnoreCase);
        });

        var toolCalls = exchanges
            .Where(exchange =>
                string.Equals(exchange.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(exchange.GetRequestHeader("Mcp-Method"), McpMethods.ToolsCall, StringComparison.Ordinal))
            .Select(exchange => new
            {
                Exchange = exchange,
                Name = GetToolCallName(exchange)
            })
            .ToArray();
        Assert.All(toolCalls, call =>
        {
            Assert.True(
                string.Equals(call.Name, "inference.generate", StringComparison.Ordinal) ||
                string.Equals(call.Name, "inference.providers.list", StringComparison.Ordinal) ||
                string.Equals(call.Name, "workspace.roots.list", StringComparison.Ordinal),
                $"Unexpected tool call '{call.Name}' in chat session.");
        });

        var generateCalls = toolCalls
            .Where(call => string.Equals(call.Name, "inference.generate", StringComparison.Ordinal))
            .Select(call => call.Exchange)
            .ToArray();
        Assert.Equal(2, generateCalls.Length);
        AssertHostGenerateToolCall(generateCalls[0], initialTarget, "hello");
        AssertHostGenerateToolCall(generateCalls[1], switchedTarget, "hello again");

        var deleteExchange = Assert.Single(exchanges, exchange => string.Equals(exchange.Method, HttpMethod.Delete.Method, StringComparison.OrdinalIgnoreCase));
        AssertMcpSessionHeader(deleteExchange);
        Assert.Equal((int)HttpStatusCode.NoContent, deleteExchange.StatusCode);
    }

    private static void AssertHostGenerateToolCall(CapturedHttpExchange exchange, LiveProviderTarget target, string expectedPrompt)
    {
        AssertMcpRequestHeaders(exchange, expectedMethod: McpMethods.ToolsCall, expectedProtocolVersion: McpProtocolVersions.Current);
        AssertMcpSessionHeader(exchange);

        using var requestDocument = JsonDocument.Parse(exchange.RequestBody);
        var requestRoot = requestDocument.RootElement;
        var parameters = requestRoot.GetProperty("params");
        Assert.Equal("inference.generate", parameters.GetProperty("name").GetString());
        var arguments = parameters.GetProperty("arguments");
        Assert.Equal(target.ProviderId, arguments.GetProperty("providerId").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(target.Model, arguments.GetProperty("model").GetString(), StringComparer.Ordinal);
        Assert.Equal(expectedPrompt, arguments.GetProperty("prompt").GetString(), StringComparer.Ordinal);

        using var responseDocument = JsonDocument.Parse(exchange.ResponseBody);
        var responseRoot = responseDocument.RootElement;
        var result = responseRoot.GetProperty("result");
        var structuredContent = result.GetProperty("structuredContent");
        Assert.Equal(target.ProviderId, structuredContent.GetProperty("providerId").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(target.Model, structuredContent.GetProperty("model").GetString(), StringComparer.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(structuredContent.GetProperty("content").GetString()));
    }

    private static void AssertMcpRequestHeaders(CapturedHttpExchange exchange, string expectedMethod, string? expectedProtocolVersion)
    {
        Assert.Equal(expectedMethod, exchange.GetRequestHeader("Mcp-Method"), StringComparer.OrdinalIgnoreCase);
        if (expectedProtocolVersion is null)
        {
            Assert.Null(exchange.GetRequestHeader("MCP-Protocol-Version"));
        }
        else
        {
            Assert.Equal(expectedProtocolVersion, exchange.GetRequestHeader("MCP-Protocol-Version"), StringComparer.Ordinal);
        }
    }

    private static void AssertMcpSessionHeader(CapturedHttpExchange exchange)
    {
        Assert.False(string.IsNullOrWhiteSpace(exchange.GetRequestHeader("MCP-Session-Id")));
    }

    private static bool TryGetToolsArray(string responseBody, out JsonElement tools)
    {
        tools = default;

        using var responseDocument = JsonDocument.Parse(responseBody);
        var responseRoot = responseDocument.RootElement;
        if (!responseRoot.TryGetProperty("result"u8, out var result))
        {
            return false;
        }

        if (!result.TryGetProperty("tools"u8, out tools) || tools.ValueKind != JsonValueKind.Array)
        {
            tools = default;
            return false;
        }

        tools = tools.Clone();
        return true;
    }

    private static CapturedHttpExchange FindSingleToolCallExchange(IReadOnlyList<CapturedHttpExchange> exchanges, string toolName)
    {
        return Assert.Single(exchanges, exchange =>
        {
            if (!string.Equals(exchange.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(exchange.GetRequestHeader("Mcp-Method"), McpMethods.ToolsCall, StringComparison.Ordinal))
            {
                return false;
            }

            using var requestDocument = JsonDocument.Parse(exchange.RequestBody);
            var requestRoot = requestDocument.RootElement;
            var parameters = requestRoot.GetProperty("params");
            return string.Equals(parameters.GetProperty("name").GetString(), toolName, StringComparison.Ordinal);
        });
    }

    private static string GetToolCallName(CapturedHttpExchange exchange)
    {
        using var requestDocument = JsonDocument.Parse(exchange.RequestBody);
        var requestRoot = requestDocument.RootElement;
        var parameters = requestRoot.GetProperty("params");
        return parameters.GetProperty("name").GetString() ?? string.Empty;
    }

    private static void AssertProviderProbeResult(
        string stdout,
        string providerId,
        string expectedStatus,
        string expectedEndpoint,
        int expectedHttpStatusCode)
    {
        Assert.True(TryGetStructuredContent(stdout, out var structuredContent), "Expected structured content in the console output.");
        Assert.True(structuredContent.TryGetProperty("providers", out var providers) && providers.ValueKind == JsonValueKind.Array, "Expected a providers array in the structured content.");

        foreach (var provider in providers.EnumerateArray())
        {
            if (!TryGetString(provider, out var currentProviderId, "providerId") ||
                !string.Equals(currentProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.Equal(expectedStatus, provider.GetProperty("status").GetString(), StringComparer.OrdinalIgnoreCase);

            var probe = provider.GetProperty("probe");
            Assert.Equal(expectedStatus, probe.GetProperty("status").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(expectedHttpStatusCode, probe.GetProperty("httpStatusCode").GetInt32());
            Assert.Equal(expectedEndpoint, probe.GetProperty("endpoint").GetString(), StringComparer.Ordinal);
            return;
        }

        Assert.Fail($"Provider '{providerId}' was not present in the probe output.");
    }

    private static void AssertInferenceResponseMetadata(string stdout, string providerId, string model)
    {
        Assert.True(TryGetStructuredContent(stdout, out var structuredContent), "Expected structured content in the console output.");
        Assert.Equal(providerId, structuredContent.GetProperty("providerId").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(model, structuredContent.GetProperty("model").GetString(), StringComparer.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(structuredContent.GetProperty("content").GetString()));
    }

    private sealed record LiveProviderTarget(string ProviderId, Uri UpstreamBaseUri, string Model);

    private sealed record LiveInferenceTarget(string ProviderId, string Model);

    private sealed record InferenceModelCandidate(string ModelId, int DiscoveryOrder, double? SizeInBillions);

    private sealed class LiveInferenceUnavailableException : Exception
    {
        public LiveInferenceUnavailableException(string message)
            : base(message)
        {
        }
    }

    private sealed class LiveLmStudioUnavailableException : Exception
    {
        public LiveLmStudioUnavailableException(string message)
            : base(message)
        {
        }

        public LiveLmStudioUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private static bool TryGetString(JsonElement root, out string? value, params string[] propertyPath)
    {
        if (root.ValueKind != JsonValueKind.Object || propertyPath.Length == 0)
        {
            value = null;
            return false;
        }

        var current = root;
        for (var i = 0; i < propertyPath.Length; i++)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(propertyPath[i], out var next))
            {
                value = null;
                return false;
            }

            current = next;
        }

        if (current.ValueKind != JsonValueKind.String)
        {
            value = null;
            return false;
        }

        value = current.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetStructuredContent(string stdout, out JsonElement structuredContent)
    {
        structuredContent = default;

        var marker = "Structured content:";
        var markerIndex = stdout.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var remainder = stdout[(markerIndex + marker.Length)..];
        var lines = remainder.Split(["\r\n", "\n"], StringSplitOptions.None);
        var json = lines.Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        using var document = JsonDocument.Parse(json);
        structuredContent = document.RootElement.Clone();
        return true;
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
