using System.Text.Json;
using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpTaskRegistry
{
    Fin<ListTasksResult> ListTasks(string? cursor);

    Fin<GetTaskResult> GetTask(string taskId);

    Fin<JsonElement> GetTaskResultPayload(string taskId);

    Fin<CancelTaskResult> CancelTask(string taskId);
}
