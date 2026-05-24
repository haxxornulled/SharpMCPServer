using MCPServer.AgentRouter.Application.Services;
using Xunit;

namespace MCPServer.AgentRouter.Application.Tests.Architecture;

public sealed class AgentRouterApplicationBoundaryTests
{
    private static readonly string[] ForbiddenAssemblyNames =
    [
        "MCPServer.Host",
        "MCPServer.Application",
        "MCPServer.Domain",
        "MCPServer.Infrastructure",
        "MCPServer.Tools.Ssh",
        "MCPServer.UnitTests"
    ];

    [Fact]
    public void AgentRouterApplication_Does_Not_Reference_MCPServer_Runtime_Layers()
    {
        var referencedNames = typeof(DefaultAgentRunCoordinator)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static assemblyName => assemblyName.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbiddenName in ForbiddenAssemblyNames)
        {
            Assert.DoesNotContain(forbiddenName, referencedNames);
        }
    }
}
