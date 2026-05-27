using Autofac;
using MCPServer.Inference.Infrastructure.Options;
using MCPServer.Inference.Infrastructure.Providers;

namespace MCPServer.Inference.Infrastructure;

public sealed class InferenceInfrastructureModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterInstance(new McpInferenceOptions())
            .AsSelf()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<LmStudioInferenceClient>()
            .As<MCPServer.Inference.Abstractions.Interfaces.IInferenceClient>()
            .SingleInstance();

        builder.RegisterType<OllamaInferenceClient>()
            .As<MCPServer.Inference.Abstractions.Interfaces.IInferenceClient>()
            .SingleInstance();

        builder.RegisterType<AnthropicInferenceClient>()
            .As<MCPServer.Inference.Abstractions.Interfaces.IInferenceClient>()
            .SingleInstance();
    }
}
