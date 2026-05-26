using Autofac;
using MCPServer.Application.Mcp.Handlers;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.Resources;
using MCPServer.Application.Mcp.Prompts;
using MCPServer.Application.Mcp.Tools;
using MCPServer.Application.Mcp;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application;

public sealed class ApplicationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterType<McpSessionState>()
            .As<IMcpSessionState>()
            .SingleInstance();

        builder.RegisterInstance(new McpRequestExecutionOptions())
            .SingleInstance();

        builder.RegisterType<McpRequestExecutionRegistry>()
            .As<IMcpRequestExecutionRegistry>()
            .SingleInstance();

        builder.RegisterType<McpLoggingState>()
            .As<IMcpLoggingState>()
            .SingleInstance();

        builder.RegisterType<NoOpClientFeatureInvoker>()
            .As<IMcpClientFeatureInvoker>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<NoOpTaskStatusNotifier>()
            .As<IMcpTaskStatusNotifier>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<McpCompletionReferenceParser>()
            .As<IMcpCompletionReferenceParser>()
            .SingleInstance();

        builder.RegisterType<McpTaskRegistry>()
            .As<IMcpTaskRegistry>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<McpRequestDispatcher>()
            .As<IMcpRequestDispatcher>()
            .SingleInstance();

        RegisterMethodHandler<InitializeHandler>(builder, McpMethods.Initialize);
        RegisterMethodHandler<InitializedNotificationHandler>(builder, McpMethods.NotificationsInitialized);
        RegisterMethodHandler<CancelledNotificationHandler>(builder, McpMethods.NotificationsCancelled);
        RegisterMethodHandler<RootsListChangedNotificationHandler>(builder, McpMethods.NotificationsRootsListChanged);
        RegisterMethodHandler<PingHandler>(builder, McpMethods.Ping);
        RegisterMethodHandler<LoggingSetLevelHandler>(builder, McpMethods.LoggingSetLevel);
        RegisterMethodHandler<ToolsListHandler>(builder, McpMethods.ToolsList);
        RegisterMethodHandler<ToolsCallHandler>(builder, McpMethods.ToolsCall);
        RegisterMethodHandler<ResourcesListHandler>(builder, McpMethods.ResourcesList);
        RegisterMethodHandler<ResourcesReadHandler>(builder, McpMethods.ResourcesRead);
        RegisterMethodHandler<ResourcesSubscribeHandler>(builder, McpMethods.ResourcesSubscribe);
        RegisterMethodHandler<ResourcesUnsubscribeHandler>(builder, McpMethods.ResourcesUnsubscribe);
        RegisterMethodHandler<ResourcesTemplatesListHandler>(builder, McpMethods.ResourcesTemplatesList);
        RegisterMethodHandler<PromptsListHandler>(builder, McpMethods.PromptsList);
        RegisterMethodHandler<PromptsGetHandler>(builder, McpMethods.PromptsGet);
        RegisterMethodHandler<CompletionCompleteHandler>(builder, McpMethods.CompletionComplete);
        RegisterMethodHandler<TasksListHandler>(builder, McpMethods.TasksList);
        RegisterMethodHandler<TasksGetHandler>(builder, McpMethods.TasksGet);
        RegisterMethodHandler<TasksResultHandler>(builder, McpMethods.TasksResult);
        RegisterMethodHandler<TasksCancelHandler>(builder, McpMethods.TasksCancel);

        RegisterTool<ServerInfoTool>(builder, McpToolNames.ServerInfo);
        RegisterTool<ClientSampleTool>(builder, McpToolNames.ClientSample);
        RegisterTool<ClientElicitFormTool>(builder, McpToolNames.ClientElicitForm);
        RegisterTool<ClientElicitUrlTool>(builder, McpToolNames.ClientElicitUrl);
        RegisterResource<ServerInfoResource>(builder, McpResourceUris.ServerInfo);
        RegisterPrompt<ServerStatusPrompt>(builder, McpPromptNames.ServerStatus);

        builder.RegisterInstance(new McpToolRegistryOptions())
            .SingleInstance();

        builder.RegisterType<McpToolRegistry>()
            .As<IMcpToolRegistry>()
            .SingleInstance();

        builder.RegisterInstance(new McpResourceRegistryOptions())
            .SingleInstance();

        builder.RegisterType<McpResourceRegistry>()
            .As<IMcpResourceRegistry>()
            .SingleInstance();

        builder.RegisterType<McpResourceSubscriptionRegistry>()
            .As<IMcpResourceSubscriptionRegistry>()
            .SingleInstance();

        builder.RegisterInstance(new McpPromptRegistryOptions())
            .SingleInstance();

        builder.RegisterType<McpPromptRegistry>()
            .As<IMcpPromptRegistry>()
            .SingleInstance();
    }

    private static void RegisterMethodHandler<THandler>(ContainerBuilder builder, string method)
        where THandler : IMcpMethodHandler
    {
        builder.RegisterType<THandler>()
            .Keyed<IMcpMethodHandler>(method)
            .SingleInstance();
    }

    private static void RegisterTool<TTool>(ContainerBuilder builder, string name)
        where TTool : IMcpTool
    {
        builder.RegisterType<TTool>()
            .Keyed<IMcpTool>(name)
            .As<IMcpTool>()
            .SingleInstance();
    }
    private static void RegisterResource<TResource>(ContainerBuilder builder, string uri)
        where TResource : IMcpResource
    {
        builder.RegisterType<TResource>()
            .Keyed<IMcpResource>(uri)
            .As<IMcpResource>()
            .SingleInstance();
    }

    private static void RegisterPrompt<TPrompt>(ContainerBuilder builder, string name)
        where TPrompt : IMcpPrompt
    {
        builder.RegisterType<TPrompt>()
            .Keyed<IMcpPrompt>(name)
            .As<IMcpPrompt>()
            .SingleInstance();
    }
}
