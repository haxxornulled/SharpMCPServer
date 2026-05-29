using System.Net;
using System.Text;
using System.Text.Json;
using LanguageExt;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Infrastructure.Options;
using MCPServer.Inference.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.UnitTests.Inference.Providers;

public sealed class LmStudioInferenceClientTests
{
    [Fact]
    public async Task GenerateAsync_Writes_Local_Tuning_Fields_Into_The_OpenAI_Compatible_Request()
    {
        var options = new McpInferenceOptions();
        options.Providers["lmstudio"] = new McpInferenceProviderOptions
        {
            Enabled = true,
            BaseAddress = "http://localhost:1234/v1/",
            Model = "local-model",
            HttpClientName = "lmstudio",
            MaxTokens = 128,
            Temperature = 0.4,
            TopP = 0.92,
            TopK = 50,
            RepeatPenalty = 1.07,
            Seed = 99
        };

        using var handler = new RecordingHttpMessageHandler();
        using var httpClientFactory = new RecordingHttpClientFactory(handler);
        var client = new LmStudioInferenceClient(httpClientFactory, options, NullLogger<LmStudioInferenceClient>.Instance);

        var result = await client.GenerateAsync(
            new InferenceRequest(
                [
                    new InferenceMessage(InferenceRole.User, "hello")
                ]),
        CancellationToken.None);

        Assert.True(result.IsSucc);
        Assert.Equal(1, handler.ChatCompletionsRequests);
        Assert.Single(handler.RequestBodies);

        using var document = JsonDocument.Parse(handler.RequestBodies[0]);
        var root = document.RootElement;
        Assert.Equal("local-model", root.GetProperty("model").GetString());
        Assert.Equal(128, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.4, root.GetProperty("temperature").GetDouble(), 3);
        Assert.Equal(0.92, root.GetProperty("top_p").GetDouble(), 3);
        Assert.Equal(99, root.GetProperty("seed").GetInt32());

        Assert.True(root.TryGetProperty("top_k", out var topKProperty));
        Assert.Equal(50, topKProperty.GetInt32());
        Assert.True(root.TryGetProperty("repeat_penalty", out var repeatPenaltyProperty));
        Assert.Equal(1.07, repeatPenaltyProperty.GetDouble(), 3);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public int ChatCompletionsRequests { get; private set; }

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestUri = request.RequestUri ?? throw new InvalidOperationException("Request URI was missing.");
            if (request.Method == HttpMethod.Post &&
                requestUri.AbsolutePath.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                ChatCompletionsRequests++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                RequestBodies.Add(body);
                return CreateJsonResponse("""
                {
                  "model": "local-model",
                  "choices": [
                    {
                      "message": {
                        "content": "local response"
                      },
                      "finish_reason": "stop"
                    }
                  ],
                  "usage": {
                    "prompt_tokens": 4,
                    "completion_tokens": 6,
                    "total_tokens": 10
                  }
                }
                """, HttpStatusCode.OK);
            }

            if (request.Method == HttpMethod.Get &&
                requestUri.AbsolutePath.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse("""
                {
                  "data": [
                    { "id": "local-model" }
                  ]
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
