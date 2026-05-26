namespace MCPServer.Domain.Mcp;

public static class McpMethods
{
    public const string Initialize = "initialize";
    public const string NotificationsInitialized = "notifications/initialized";
    public const string NotificationsCancelled = "notifications/cancelled";
    public const string NotificationsProgress = "notifications/progress";
    public const string NotificationsMessage = "notifications/message";
    public const string NotificationsToolsListChanged = "notifications/tools/list_changed";
    public const string NotificationsResourcesListChanged = "notifications/resources/list_changed";
    public const string NotificationsResourcesUpdated = "notifications/resources/updated";
    public const string NotificationsPromptsListChanged = "notifications/prompts/list_changed";
    public const string NotificationsRootsListChanged = "notifications/roots/list_changed";
    public const string Ping = "ping";
    public const string LoggingSetLevel = "logging/setLevel";
    public const string ToolsList = "tools/list";
    public const string ToolsCall = "tools/call";
    public const string ResourcesList = "resources/list";
    public const string ResourcesRead = "resources/read";
    public const string ResourcesSubscribe = "resources/subscribe";
    public const string ResourcesUnsubscribe = "resources/unsubscribe";
    public const string ResourcesTemplatesList = "resources/templates/list";
    public const string PromptsList = "prompts/list";
    public const string PromptsGet = "prompts/get";
    public const string CompletionComplete = "completion/complete";
    public const string SamplingCreateMessage = "sampling/createMessage";
    public const string ElicitationCreate = "elicitation/create";
    public const string NotificationsTasksStatus = "notifications/tasks/status";
    public const string NotificationsElicitationComplete = "notifications/elicitation/complete";
    public const string TasksList = "tasks/list";
    public const string TasksGet = "tasks/get";
    public const string TasksResult = "tasks/result";
    public const string TasksCancel = "tasks/cancel";
    public const string RootsList = "roots/list";
}
