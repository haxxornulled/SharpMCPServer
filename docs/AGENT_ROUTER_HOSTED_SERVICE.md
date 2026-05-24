# AgentRouter Hosted Lifecycle Service Boundary

The AgentRouter owns its long-running worker loop. MCPServer consumes that loop as a hosted service; MCPServer should not directly drive the internal agent loop.

## Direction

```text
MCPServer.Host
  registers AgentRouter provider modules through Autofac
  runs AgentRouterBackgroundService through the .NET hosted-service lifecycle

MCPServer.AgentRouter.Hosting
  BackgroundService + IHostedLifecycleService adapter
  -> AgentRouter.Application worker port

MCPServer.AgentRouter.Application
  owns worker use cases and ports

MCPServer.AgentRouter.Infrastructure
  later supplies adapters for store/trace/tool/model execution ports
```

## Lifecycle

`AgentRouterBackgroundService` derives from `BackgroundService` and implements `IHostedLifecycleService`.

The lifecycle hooks are used for hosted-service readiness and shutdown boundaries:

```text
StartingAsync
StartAsync / ExecuteAsync
StartedAsync
StoppingAsync
StopAsync
StoppedAsync
```

The worker loop itself belongs to AgentRouter. MCPServer.Host only composes and runs it.

## Rule

The AgentRouter hosted service must not reference these runtime layers:

```text
MCPServer.Host
MCPServer.Application
MCPServer.Domain
MCPServer.Infrastructure
MCPServer.Tools.Ssh
MCPServer.UnitTests
```

MCPServer is the consumer/composition root. AgentRouter is the provider-side worker package.

## Autofac registration sketch

MCPServer consumes the AgentRouter hosted service by registering AgentRouter modules in the Host composition root:

```csharp
containerBuilder.RegisterModule(new AgentRouterApplicationModule());
containerBuilder.RegisterModule(new AgentRouterInfrastructureModule());
containerBuilder.RegisterModule(new AgentRouterHostingModule());
```

`AgentRouterHostingModule` registers `AgentRouterBackgroundService` as `IHostedService` and `IHostedLifecycleService`. The .NET host runs it through the normal hosted-service pipeline, and lifecycle-aware hosts can invoke the richer lifecycle hooks.

## Current slice

This slice adds the hosted lifecycle seam only:

```text
IAgentRouterWorker
DefaultAgentRouterWorker
AgentRouterBackgroundService
AgentRouterHostingModule
MCPServer.Host consumption through Autofac
```

The default worker currently runs an idle cycle. Planning and execution remain disabled until the next slice adds queue draining and application orchestration.
