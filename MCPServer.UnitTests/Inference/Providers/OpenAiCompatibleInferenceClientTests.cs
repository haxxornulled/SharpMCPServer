using System.Net;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using LanguageExt;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Infrastructure.Options;
using MCPServer.Inference.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Http;
using Xunit;

namespace MCPServer.UnitTests.Inference.Providers;

public sealed class OpenAiCompatibleInferenceClientTests
{
    [Fact]
    public async Task GenerateAsync_Resolves_A_Discovered_Model_When_The_Configured_Model_Is_Missing()
    {
        var options = new McpInferenceOptions();
        options.Providers["ollama"] = new McpInferenceProviderOptions
        {
            Enabled = true,
            BaseAddress = "http://localhost:11434/v1/",
            Model = "llama3.1",
            HttpClientName = "ollama",
            MaxTokens = 256,
            Temperature = 0.2,
            TopP = 0.9,
            TopK = 40,
            RepeatPenalty = 1.1,
            Seed = 42,
            ContextLength = 8192,
            KeepAlive = "5m"
        };

        using var handler = new ModelDiscoveryRetryHttpMessageHandler();
        using var httpClientFactory = new RecordingHttpClientFactory(handler);
        var client = new OllamaInferenceClient(httpClientFactory, options, NullLogger<OllamaInferenceClient>.Instance);

        var result = await client.GenerateAsync(
            new InferenceRequest(
                [
                    new InferenceMessage(InferenceRole.User, "hello")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSucc);

        var response = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.Equal("ollama", response.ProviderId);
        Assert.Equal("installed-model", response.Model);
        Assert.Equal("fallback response", response.Content);
        Assert.Equal("stop", response.FinishReason);
        Assert.NotNull(response.Metadata);
        Assert.True(response.Metadata!.TryGetValue("generationElapsedMilliseconds", out var elapsedRaw));
        Assert.True(int.TryParse(elapsedRaw, out var elapsedMilliseconds));
        Assert.True(elapsedMilliseconds >= 1);
        Assert.True(response.Metadata.TryGetValue("tokensPerSecond", out var tokensPerSecondRaw));
        Assert.True(double.TryParse(tokensPerSecondRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var tokensPerSecond));
        Assert.True(tokensPerSecond > 0);
        Assert.True(response.Metadata.ContainsKey("inputTokensPerSecond"));
        Assert.True(response.Metadata.ContainsKey("outputTokensPerSecond"));
        Assert.True(response.Metadata.ContainsKey("loadDurationMilliseconds"));
        Assert.True(response.Metadata.ContainsKey("promptEvalDurationMilliseconds"));
        Assert.True(response.Metadata.ContainsKey("evalDurationMilliseconds"));
        Assert.Equal(2, handler.ChatCompletionsRequests);
        Assert.Equal(1, handler.ModelsRequests);
        Assert.Equal(["llama3.1", "installed-model"], handler.RequestedModels);
        Assert.Contains("5m", handler.RequestedKeepAlives);
        Assert.Contains(8192, handler.RequestedNumCtx);
        Assert.Contains(256, handler.RequestedNumPredict);
        Assert.Contains(0.9, handler.RequestedTopP);
        Assert.Contains(40, handler.RequestedTopK);
        Assert.Contains(1.1, handler.RequestedRepeatPenalty);
        Assert.Contains(42, handler.RequestedSeed);
    }

    [Fact]
    public async Task GenerateAsync_Does_Not_Override_An_Explicit_Request_Model()
    {
        var options = new McpInferenceOptions();
        options.Providers["ollama"] = new McpInferenceProviderOptions
        {
            Enabled = true,
            BaseAddress = "http://localhost:11434/v1/",
            Model = "llama3.1",
            HttpClientName = "ollama"
        };

        using var handler = new ModelDiscoveryRetryHttpMessageHandler();
        using var httpClientFactory = new RecordingHttpClientFactory(handler);
        var client = new OllamaInferenceClient(httpClientFactory, options, NullLogger<OllamaInferenceClient>.Instance);

        var result = await client.GenerateAsync(
            new InferenceRequest(
                [
                    new InferenceMessage(InferenceRole.User, "hello")
                ],
                Model: "explicit-model"),
            CancellationToken.None);

        Assert.True(result.IsFail);
        var failure = result.Match(
            Succ: static _ => throw new Xunit.Sdk.XunitException("Expected the request to fail."),
            Fail: static error => error.Message);

        Assert.Contains("model 'explicit-model' not found", failure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.ChatCompletionsRequests);
        Assert.Equal(0, handler.ModelsRequests);
        Assert.Equal(["explicit-model"], handler.RequestedModels);
    }

    private sealed class ModelDiscoveryRetryHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<string> _requestedModels = [];
        private readonly List<int> _requestedNumCtx = [];
        private readonly List<int> _requestedNumPredict = [];
        private readonly List<double> _requestedTopP = [];
        private readonly List<int> _requestedTopK = [];
        private readonly List<double> _requestedRepeatPenalty = [];
        private readonly List<int> _requestedSeed = [];
        private readonly List<string> _requestedKeepAlives = [];

        public int ChatCompletionsRequests { get; private set; }

        public int ModelsRequests { get; private set; }

        public IReadOnlyList<string> RequestedModels => _requestedModels;

        public IReadOnlyList<int> RequestedNumCtx => _requestedNumCtx;

        public IReadOnlyList<int> RequestedNumPredict => _requestedNumPredict;

        public IReadOnlyList<double> RequestedTopP => _requestedTopP;

        public IReadOnlyList<int> RequestedTopK => _requestedTopK;

        public IReadOnlyList<double> RequestedRepeatPenalty => _requestedRepeatPenalty;

        public IReadOnlyList<int> RequestedSeed => _requestedSeed;

        public IReadOnlyList<string> RequestedKeepAlives => _requestedKeepAlives;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestUri = request.RequestUri ?? throw new InvalidOperationException("Request URI was missing.");
            if (request.Method == HttpMethod.Get &&
                requestUri.AbsolutePath.EndsWith("/api/tags", StringComparison.OrdinalIgnoreCase))
            {
                ModelsRequests++;
                return CreateJsonResponse("""
                {
                  "models": [
                    { "name": "installed-model" },
                    { "name": "secondary-model" }
                  ]
                }
                """, HttpStatusCode.OK);
            }

            if (request.Method == HttpMethod.Post &&
                requestUri.AbsolutePath.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            {
                ChatCompletionsRequests++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(body);
                var model = document.RootElement.GetProperty("model").GetString();
                _requestedModels.Add(model ?? string.Empty);
                RecordRequestOptions(document.RootElement);

                if (ChatCompletionsRequests == 1)
                {
                    return CreateJsonResponse(
                        $$"""
                        {
                          "error": {
                            "message": "model '{{model}}' not found"
                          }
                        }
                        """,
                        HttpStatusCode.NotFound);
                }

                return CreateJsonResponse("""
                {
                  "model": "installed-model",
                  "message": {
                    "role": "assistant",
                    "content": "fallback response"
                  },
                  "done_reason": "stop",
                  "prompt_eval_count": 4,
                  "eval_count": 6,
                  "total_duration": 1000000000,
                  "load_duration": 200000000,
                  "prompt_eval_duration": 300000000,
                  "eval_duration": 500000000
                }
                """, HttpStatusCode.OK);
            }

            return CreateJsonResponse("""
            {
              "error": {
                "message": "unexpected request"
              }
            }
            """, HttpStatusCode.NotFound);
        }

        private void RecordRequestOptions(JsonElement root)
        {
            if (root.TryGetProperty("keep_alive", out var keepAliveElement) &&
                keepAliveElement.ValueKind == JsonValueKind.String)
            {
                _requestedKeepAlives.Add(keepAliveElement.GetString() ?? string.Empty);
            }

            if (!root.TryGetProperty("options", out var optionsElement) ||
                optionsElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (optionsElement.TryGetProperty("num_ctx", out var numCtxElement) &&
                numCtxElement.TryGetInt32(out var numCtx))
            {
                _requestedNumCtx.Add(numCtx);
            }

            if (optionsElement.TryGetProperty("num_predict", out var numPredictElement) &&
                numPredictElement.TryGetInt32(out var numPredict))
            {
                _requestedNumPredict.Add(numPredict);
            }

            if (optionsElement.TryGetProperty("top_p", out var topPElement) &&
                topPElement.TryGetDouble(out var topP))
            {
                _requestedTopP.Add(topP);
            }

            if (optionsElement.TryGetProperty("top_k", out var topKElement) &&
                topKElement.TryGetInt32(out var topK))
            {
                _requestedTopK.Add(topK);
            }

            if (optionsElement.TryGetProperty("repeat_penalty", out var repeatPenaltyElement) &&
                repeatPenaltyElement.TryGetDouble(out var repeatPenalty))
            {
                _requestedRepeatPenalty.Add(repeatPenalty);
            }

            if (optionsElement.TryGetProperty("seed", out var seedElement) &&
                seedElement.TryGetInt32(out var seed))
            {
                _requestedSeed.Add(seed);
            }
        }

        private static HttpResponseMessage CreateJsonResponse(string json, HttpStatusCode statusCode)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _client;

        public RecordingHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler, disposeHandler: false);
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
