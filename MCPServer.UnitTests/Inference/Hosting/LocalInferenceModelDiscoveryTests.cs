using MCPServer.Inference.Infrastructure.Hosting;
using Xunit;

namespace MCPServer.UnitTests.Inference.Hosting;

public sealed class LocalInferenceModelDiscoveryTests
{
    [Fact]
    public void ParseLmStudioModels_Collects_Installed_Llm_Descriptors()
    {
        const string json = """
        {
          "models": [
            {
              "modelKey": "openai/gpt-oss-20b",
              "displayName": "gpt-oss 20B",
              "sizeBytes": 123456789
            },
            {
              "name": "small-model",
              "displayName": "Small Model",
              "size": 9876543
            }
          ]
        }
        """;

        var models = LocalInferenceModelDiscovery.ParseLmStudioModels(json);

        Assert.Equal(2, models.Count);
        Assert.Equal("openai/gpt-oss-20b", models[0].ModelKey);
        Assert.Equal("gpt-oss 20B", models[0].DisplayName);
        Assert.Equal(123456789, models[0].SizeBytes);
        Assert.Equal("small-model", models[1].ModelKey);
        Assert.Equal("Small Model", models[1].DisplayName);
        Assert.Equal(9876543, models[1].SizeBytes);
    }

    [Fact]
    public void SelectPreferredModel_Uses_The_Configured_Model_When_It_Is_Installed()
    {
        var models = new[]
        {
            new LocalInferenceModelDescriptor("openai/gpt-oss-20b", "gpt-oss 20B", 123456789),
            new LocalInferenceModelDescriptor("small-model", "Small Model", 9876543)
        };

        var selected = LocalInferenceModelDiscovery.SelectPreferredModel(models, "gpt-oss 20b");

        Assert.Equal("openai/gpt-oss-20b", selected);
    }

    [Fact]
    public void SelectPreferredModel_Falls_Back_To_The_Smallest_Installed_Model_When_The_Configured_One_Is_Missing()
    {
        var models = new[]
        {
            new LocalInferenceModelDescriptor("large-model", "Large Model", 123456789),
            new LocalInferenceModelDescriptor("small-model", "Small Model", 9876543)
        };

        var selected = LocalInferenceModelDiscovery.SelectPreferredModel(models, "missing-model");

        Assert.Equal("small-model", selected);
    }
}
