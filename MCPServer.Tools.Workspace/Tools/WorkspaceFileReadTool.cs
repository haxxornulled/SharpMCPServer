using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Workspace;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Tools.Workspace.Tools;

public sealed class WorkspaceFileReadTool : IMcpTool
{
    private static readonly JsonElement InputSchema = WorkspaceToolSchemas.CreateFileReadInputSchema();
    private static readonly JsonElement OutputSchema = WorkspaceToolSchemas.CreateFileReadOutputSchema();

    private readonly IWorkspaceFileService _workspaceFileService;

    public WorkspaceFileReadTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService ?? throw new ArgumentNullException(nameof(workspaceFileService));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = WorkspaceToolNames.FilesRead,
        Title = "Read Workspace File",
        Description = "Reads a workspace-scoped text file while enforcing root boundaries.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution
        {
            TaskSupport = McpToolTaskSupport.Forbidden
        }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestResult = WorkspaceToolArguments.Parse(arguments, WorkspaceJsonSerializerContext.Default.WorkspaceFileReadRequest, WorkspaceToolNames.FilesRead);
        if (requestResult.IsFail)
        {
            var error = requestResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workspace argument parse success while handling failure."),
                Fail: static value => value);
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true));
        }

        var request = requestResult.Match(
            Succ: static value => value,
            Fail: error => throw new InvalidOperationException($"Unexpected workspace file read argument parse failure: {error.Message}"));
        if (WorkspaceToolArguments.RequireString(request.RootName, "rootName", WorkspaceToolNames.FilesRead).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.FilesRead} requires a string rootName.", isError: true));
        }

        if (WorkspaceToolArguments.RequireString(request.RelativePath, "relativePath", WorkspaceToolNames.FilesRead).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.FilesRead} requires a string relativePath.", isError: true));
        }

        var result = await _workspaceFileService.ReadFileAsync(request.RootName.Trim(), request.RelativePath.Trim(), cancellationToken).ConfigureAwait(false);
        return result.Match<Fin<ToolCallResult>>(
            Succ: value =>
            {
                var structuredContent = JsonSerializer.SerializeToElement(value, WorkspaceJsonSerializerContext.Default.WorkspaceFileReadResult);
                return Fin.Succ<ToolCallResult>(ToolCallResult.Text(
                    $"Read {value.BytesRead} bytes from '{value.RelativePath}'.",
                    structuredContent: structuredContent));
            },
            Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
    }
}
