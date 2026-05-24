using Autofac;
using LanguageExt;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Defaults.Tests.Testing;
using Xunit;

namespace MCPServer.AgentRouter.Defaults.Tests.Routing;

public sealed class AgentRouterDefaultsModuleTests
{
    [Fact]
    public void AgentRouterDefaultsModule_Resolves_Provider_Selector_Route_And_Default_Router()
    {
        using var container = BuildContainer();

        var provider = container.Resolve<IAgentRouterProvider>();
        var selector = container.Resolve<IAgentRouterSelector>();
        var router = container.Resolve<IAgentRouter>();
        var routes = container.Resolve<IEnumerable<IAgentRouterRoute>>();

        Assert.NotNull(provider);
        Assert.NotNull(selector);
        Assert.NotNull(router);
        Assert.Contains(routes, static route => string.Equals(route.Name, AgentRouterNames.Default, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DefaultProvider_Returns_Default_Router()
    {
        using var container = BuildContainer();
        var provider = container.Resolve<IAgentRouterProvider>();
        var request = AgentRouterProviderRequest.Default;

        var router = TestFin.Success(await provider.GetRouterAsync(in request, TestContext.Current.CancellationToken));

        Assert.Same(container.Resolve<IAgentRouter>(), router);
    }

    [Fact]
    public async Task DefaultProvider_Returns_Default_Router_When_Router_Name_Is_Empty()
    {
        using var container = BuildContainer();
        var provider = container.Resolve<IAgentRouterProvider>();
        var request = new AgentRouterProviderRequest(
            RouterName: null,
            Metadata: AgentRouterMetadata.Empty);

        var router = TestFin.Success(await provider.GetRouterAsync(in request, TestContext.Current.CancellationToken));

        Assert.Same(container.Resolve<IAgentRouter>(), router);
    }

    [Fact]
    public async Task DefaultProvider_Returns_NoOp_Router_When_NoOp_Alias_Is_Requested()
    {
        using var container = BuildContainer();
        var provider = container.Resolve<IAgentRouterProvider>();
        var request = new AgentRouterProviderRequest(
            RouterName: AgentRouterNames.NoOp,
            Metadata: AgentRouterMetadata.Empty);

        var router = TestFin.Success(await provider.GetRouterAsync(in request, TestContext.Current.CancellationToken));

        Assert.Same(container.Resolve<IAgentRouter>(), router);
    }

    [Fact]
    public async Task DefaultProvider_Rejects_Unknown_Router_Name()
    {
        using var container = BuildContainer();
        var provider = container.Resolve<IAgentRouterProvider>();
        var request = new AgentRouterProviderRequest(
            RouterName: "unknown",
            Metadata: AgentRouterMetadata.Empty);

        var failure = TestFin.Failure(await provider.GetRouterAsync(in request, TestContext.Current.CancellationToken));

        Assert.Contains("not registered", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DefaultProvider_Selects_Custom_Route_Registered_By_Another_Package()
    {
        using var container = BuildContainerWithCustomRoute();
        var provider = container.Resolve<IAgentRouterProvider>();
        var request = new AgentRouterProviderRequest(
            RouterName: CustomAgentRouterRoute.CustomRouterName,
            Metadata: AgentRouterMetadata.Empty);

        var router = TestFin.Success(await provider.GetRouterAsync(in request, TestContext.Current.CancellationToken));

        Assert.IsType<CustomAgentRouter>(router);
    }

    [Fact]
    public async Task NoOpRouter_Returns_Controlled_Disabled_Result()
    {
        using var container = BuildContainer();
        var router = container.Resolve<IAgentRouter>();
        var request = new AgentRouterRunRequest(
            Objective: "validate packaged provider seam",
            Metadata: AgentRouterMetadata.Empty);

        var result = TestFin.Success(await router.RunAsync(in request, TestContext.Current.CancellationToken));

        Assert.Equal(AgentRouterRunStatuses.Disabled, result.Status);
        Assert.Contains("not enabled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoOpRouter_Rejects_Missing_Objective()
    {
        using var container = BuildContainer();
        var router = container.Resolve<IAgentRouter>();
        var request = new AgentRouterRunRequest(
            Objective: null,
            Metadata: AgentRouterMetadata.Empty);

        var failure = TestFin.Failure(await router.RunAsync(in request, TestContext.Current.CancellationToken));

        Assert.Contains("objective", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new AgentRouterDefaultsModule());
        return builder.Build();
    }

    private static IContainer BuildContainerWithCustomRoute()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new AgentRouterDefaultsModule());
        builder.RegisterType<CustomAgentRouter>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<CustomAgentRouterRoute>()
            .As<IAgentRouterRoute>()
            .SingleInstance();
        return builder.Build();
    }

    private sealed class CustomAgentRouterRoute : IAgentRouterRoute
    {
        internal const string CustomRouterName = "custom";

        private readonly CustomAgentRouter _router;

        public CustomAgentRouterRoute(CustomAgentRouter router)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
        }

        public string Name => CustomRouterName;

        public int Order => -100;

        public bool IsMatch(in AgentRouterProviderRequest request)
        {
            return string.Equals(request.RouterName, CustomRouterName, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<Fin<IAgentRouter>> GetRouterAsync(
            in AgentRouterProviderRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<IAgentRouter>>(Fin.Succ<IAgentRouter>(_router));
        }
    }

    private sealed class CustomAgentRouter : IAgentRouter
    {
        public ValueTask<Fin<AgentRouterRunResult>> RunAsync(
            in AgentRouterRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timestamp = DateTimeOffset.UtcNow;
            var result = new AgentRouterRunResult(
                Status: AgentRouterRunStatuses.Completed,
                Message: "Custom router executed.",
                RunId: "custom-run",
                StartedAtUtc: timestamp,
                CompletedAtUtc: timestamp);

            return new ValueTask<Fin<AgentRouterRunResult>>(Fin.Succ(result));
        }
    }
}
