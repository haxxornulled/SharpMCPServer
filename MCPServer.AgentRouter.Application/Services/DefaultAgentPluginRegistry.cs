using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class DefaultAgentPluginRegistry : IAgentPluginRegistry
{
    private readonly IReadOnlyList<IAgentPlugin> _plugins;

    public DefaultAgentPluginRegistry(IEnumerable<IAgentPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        _plugins = plugins
            .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<IAgentPlugin> Plugins => _plugins;

    public ValueTask<Fin<IAgentPlugin>> SelectAsync(
        AgentPluginExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.CapabilityName))
        {
            return new ValueTask<Fin<IAgentPlugin>>(
                Fin.Fail<IAgentPlugin>(Error.New("Agent capability name is required.")));
        }

        foreach (var plugin in _plugins)
        {
            if (plugin.CanHandle(request))
            {
                return new ValueTask<Fin<IAgentPlugin>>(Fin.Succ(plugin));
            }
        }

        return new ValueTask<Fin<IAgentPlugin>>(
            Fin.Fail<IAgentPlugin>(Error.New($"No AgentRouter plugin can handle capability '{request.CapabilityName}'.")));
    }
}
