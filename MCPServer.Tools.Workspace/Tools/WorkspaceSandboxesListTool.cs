using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Workspace;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Tools.Workspace.Tools;

public sealed class WorkspaceSandboxesListTool : IMcpTool
{
    private static readonly JsonElement InputSchema = WorkspaceToolSchemas.CreateRootsListInputSchema();
    private static readonly JsonElement OutputSchema = WorkspaceToolSchemas.CreateSandboxesListOutputSchema();

    private readonly IWorkspaceSandboxCatalog _sandboxCatalog;

    public WorkspaceSandboxesListTool(IWorkspaceSandboxCatalog sandboxCatalog)
    {
        _sandboxCatalog = sandboxCatalog ?? throw new ArgumentNullException(nameof(sandboxCatalog));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = WorkspaceToolNames.SandboxesList,
        Title = "List Workspace Sandboxes",
        Description = "Lists active workspace sandboxes.",
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

        var validation = WorkspaceToolArguments.RequireNoArguments(arguments, WorkspaceToolNames.SandboxesList);
        if (validation.IsFail)
        {
            var error = validation.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workspace argument validation success while handling failure."),
                Fail: static value => value);
            return new ValueTask<Fin<ToolCallResult>>(Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
        }

        var sandboxes = new WorkspaceSandboxesListResult
        {
            Sandboxes = _sandboxCatalog.GetSandboxes().ToList()
        };

        var text = sandboxes.Sandboxes.Count == 1
            ? "1 workspace sandbox active."
            : $"{sandboxes.Sandboxes.Count} workspace sandboxes active.";

        var structuredContent = JsonSerializer.SerializeToElement(sandboxes, WorkspaceJsonSerializerContext.Default.WorkspaceSandboxesListResult);
        return new ValueTask<Fin<ToolCallResult>>(Fin.Succ<ToolCallResult>(ToolCallResult.Text(text, structuredContent: structuredContent)));
    }
}
