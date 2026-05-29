using Autofac;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.Tools;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class AgentRouterToolsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RegisterTool<AgentCreateTool>(builder, AgentToolNames.Create);
        RegisterTool<AgentSubagentCreateTool>(builder, AgentToolNames.SubagentCreate);
        RegisterTool<AgentStatusTool>(builder, AgentToolNames.Status);
        RegisterTool<AgentApproveTool>(builder, AgentToolNames.Approve);
        RegisterTool<AgentCancelTool>(builder, AgentToolNames.Cancel);
    }

    private static void RegisterTool<TTool>(ContainerBuilder builder, string name)
        where TTool : IMcpTool
    {
        builder.RegisterType<TTool>()
            .Keyed<IMcpTool>(name)
            .As<IMcpTool>()
            .InstancePerMatchingLifetimeScope(McpLifetimeScopeTags.Session);
    }
}
