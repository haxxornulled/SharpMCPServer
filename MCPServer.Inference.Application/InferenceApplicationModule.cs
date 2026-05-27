using Autofac;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Application.Options;
using MCPServer.Inference.Application.Services;

namespace MCPServer.Inference.Application;

public sealed class InferenceApplicationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterInstance(InferenceRoutingOptions.Default)
            .AsSelf()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<InferenceRoutePlanner>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DefaultInferenceRouter>()
            .As<IInferenceRouter>()
            .SingleInstance();
    }
}
