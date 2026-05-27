using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Workspace;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Tools.Workspace.Tools;

public sealed class WorkspaceRootsListTool : IMcpTool
{
    private static readonly JsonElement InputSchema = WorkspaceToolSchemas.CreateRootsListInputSchema();
    private static readonly JsonElement OutputSchema = WorkspaceToolSchemas.CreateRootsListOutputSchema();

    private readonly IWorkspaceRootCatalog _rootCatalog;

    public WorkspaceRootsListTool(IWorkspaceRootCatalog rootCatalog)
    {
        _rootCatalog = rootCatalog ?? throw new ArgumentNullException(nameof(rootCatalog));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = WorkspaceToolNames.RootsList,
        Title = "List Workspace Roots",
        Description = "Lists configured workspace roots and whether each root is writable.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution
        {
            TaskSupport = McpToolTaskSupport.Forbidden
        }
    };

    public ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = WorkspaceToolArguments.RequireNoArguments(arguments, WorkspaceToolNames.RootsList);
        if (validation.IsFail)
        {
            var error = validation.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workspace argument validation success while handling failure."),
                Fail: static value => value);
            return new ValueTask<Fin<ToolCallResult>>(Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
        }

        var result = new WorkspaceRootsListResult
        {
            Roots = _rootCatalog.GetRoots().ToList()
        };

        var text = result.Roots.Count == 1
            ? "1 workspace root configured."
            : $"{result.Roots.Count} workspace roots configured.";

        var structuredContent = JsonSerializer.SerializeToElement(result, WorkspaceJsonSerializerContext.Default.WorkspaceRootsListResult);
        return new ValueTask<Fin<ToolCallResult>>(Fin.Succ<ToolCallResult>(ToolCallResult.Text(text, structuredContent: structuredContent)));
    }
}
