using Autofac;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Tools.Inference.Tools;

namespace MCPServer.Tools.Inference;

public sealed class InferenceToolsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RegisterTool<InferenceGenerateTool>(builder, InferenceToolNames.Generate);
        RegisterTool<InferenceProvidersListTool>(builder, InferenceToolNames.ProvidersList);
    }

    private static void RegisterTool<TTool>(ContainerBuilder builder, string name)
        where TTool : IMcpTool
    {
        builder.RegisterType<TTool>()
            .Keyed<IMcpTool>(name)
            .As<IMcpTool>()
            .SingleInstance();
    }
}
