using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Infrastructure.Hosting;
using MCPServer.Inference.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using Xunit;

namespace MCPServer.UnitTests.Inference.Hosting;

public sealed class LocalInferenceProviderBootstrapServiceTests
{
    [Fact]
    public async Task StartAsync_Launches_Only_The_Loopback_Local_Providers_When_They_Are_Unreachable()
    {
        var options = new McpInferenceOptions();
        options.Providers["lmstudio"] = new McpInferenceProviderOptions
        {
            Enabled = true,
            BaseAddress = "http://127.0.0.1:1234/v1/",
            Model = "local-model",
            HttpClientName = "lmstudio"
        };
        options.Providers["ollama"] = new McpInferenceProviderOptions
        {
            Enabled = true,
            BaseAddress = "http://localhost:11434/v1/",
            Model = "llama3.1",
            HttpClientName = "ollama"
        };
        options.Providers["openai"] = new McpInferenceProviderOptions
        {
            Enabled = true,
            BaseAddress = "https://api.openai.com/v1/",
            Model = "gpt-5.5",
            HttpClientName = "openai",
            ApiKey = "test-key"
        };

        var clients = new IInferenceClient[]
        {
            new FakeInferenceClient("lmstudio", InferenceProviderProbeResult.Unreachable("lmstudio", "LM Studio", 503, 12, "unavailable", "http://127.0.0.1:1234/v1/models")),
            new FakeInferenceClient("ollama", InferenceProviderProbeResult.Unreachable("ollama", "Ollama", 503, 15, "unavailable", "http://localhost:11434/api/tags")),
            new FakeInferenceClient("openai", InferenceProviderProbeResult.Ready("openai", "OpenAI", 200, 1, "https://api.openai.com/v1/models"))
        };

        var launcher = new RecordingLauncher();
        var service = new LocalInferenceProviderBootstrapService(
            clients,
            new FakeHttpClientFactory(),
            options,
            launcher,
            NullLogger<LocalInferenceProviderBootstrapService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["lmstudio", "ollama"], launcher.StartedProviderIds);
    }

    [Fact]
    public void ProcessStartInfoFactory_Wires_Performance_Settings_For_Ollama_And_LM_Studio()
    {
        var ollamaOptions = new McpInferenceProviderOptions
        {
            ContextLength = 4096,
            KeepAlive = "10m"
        };

        Assert.True(LocalInferenceProviderProcessStartInfoFactory.TryCreateOllamaStartInfo(
            @"C:\Program Files\Ollama\ollama.exe",
            new Uri("http://127.0.0.1:11434/v1/"),
            ollamaOptions,
            out var ollamaStartInfo));

        Assert.Equal("serve", ollamaStartInfo.ArgumentList[0]);
        Assert.Equal("127.0.0.1:11434", ollamaStartInfo.Environment["OLLAMA_HOST"]);
        Assert.Equal("4096", ollamaStartInfo.Environment["OLLAMA_CONTEXT_LENGTH"]);
        Assert.Equal("10m", ollamaStartInfo.Environment["OLLAMA_KEEP_ALIVE"]);
        Assert.Equal("1", ollamaStartInfo.Environment["OLLAMA_FLASH_ATTENTION"]);
        Assert.Equal("q8_0", ollamaStartInfo.Environment["OLLAMA_KV_CACHE_TYPE"]);

        Assert.True(LocalInferenceProviderProcessStartInfoFactory.TryCreateLmStudioLoadStartInfo(
            @"C:\Users\James Arceri\.lmstudio\bin\lms.exe",
            new McpInferenceProviderOptions
            {
                Model = "openai/gpt-oss-20b",
                ContextLength = 4096
            },
            out var lmStudioLoadStartInfo));

        Assert.NotNull(lmStudioLoadStartInfo);
        Assert.Equal("load", lmStudioLoadStartInfo!.ArgumentList[0]);
        Assert.Equal("openai/gpt-oss-20b", lmStudioLoadStartInfo.ArgumentList[1]);
        Assert.Equal("--gpu", lmStudioLoadStartInfo.ArgumentList[2]);
        Assert.Equal("max", lmStudioLoadStartInfo.ArgumentList[3]);
        Assert.Equal("--context-length", lmStudioLoadStartInfo.ArgumentList[4]);
        Assert.Equal("4096", lmStudioLoadStartInfo.ArgumentList[5]);
    }

    private sealed class FakeInferenceClient : IInferenceClient
    {
        private readonly InferenceProviderProbeResult _probeResult;

        public FakeInferenceClient(string providerId, InferenceProviderProbeResult probeResult)
        {
            ProviderId = providerId;
            _probeResult = probeResult;
            Descriptor = new InferenceProviderDescriptor(providerId, providerId.ToUpperInvariant(), true, false);
        }

        public string ProviderId { get; }

        public InferenceProviderDescriptor Descriptor { get; }

        public ValueTask<InferenceProviderProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_probeResult);
        }

        public ValueTask<LanguageExt.Fin<InferenceResponse>> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingLauncher : ILocalInferenceProviderLauncher
    {
        private readonly List<string> _startedProviderIds = [];

        public IReadOnlyList<string> StartedProviderIds => _startedProviderIds;

        public ValueTask<ILocalInferenceProviderHandle?> StartAsync(
            string providerId,
            McpInferenceProviderOptions providerOptions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _startedProviderIds.Add(providerId);
            return ValueTask.FromResult<ILocalInferenceProviderHandle?>(new NoOpHandle());
        }

        private sealed class NoOpHandle : ILocalInferenceProviderHandle
        {
            public ValueTask StopAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            _ = name;
            return new HttpClient(new HttpClientHandler(), disposeHandler: true);
        }
    }
}
