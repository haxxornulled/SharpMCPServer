using LanguageExt;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application.Models;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentModelContextBuilder
{
    Fin<AgentModelContext> Build(in AgentRouterRunRequest request);
}
