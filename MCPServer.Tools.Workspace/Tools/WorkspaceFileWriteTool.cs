using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Workspace;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Tools.Workspace.Tools;

public sealed class WorkspaceFileWriteTool : IMcpTool
{
    private static readonly JsonElement InputSchema = WorkspaceToolSchemas.CreateFileWriteInputSchema();
    private static readonly JsonElement OutputSchema = WorkspaceToolSchemas.CreateFileWriteOutputSchema();

    private readonly IWorkspaceFileService _workspaceFileService;

    public WorkspaceFileWriteTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService ?? throw new ArgumentNullException(nameof(workspaceFileService));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = WorkspaceToolNames.FilesWrite,
        Title = "Write Workspace File",
        Description = "Writes a text file inside a configured writable workspace root.",
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

        var requestResult = WorkspaceToolArguments.Parse(arguments, WorkspaceJsonSerializerContext.Default.WorkspaceFileWriteRequest, WorkspaceToolNames.FilesWrite);
        if (requestResult.IsFail)
        {
            var error = requestResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workspace argument parse success while handling failure."),
                Fail: static value => value);
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true));
        }

        var request = requestResult.Match(
            Succ: static value => value,
            Fail: error => throw new InvalidOperationException($"Unexpected workspace file write argument parse failure: {error.Message}"));
        if (WorkspaceToolArguments.RequireString(request.RootName, "rootName", WorkspaceToolNames.FilesWrite).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.FilesWrite} requires a string rootName.", isError: true));
        }

        if (WorkspaceToolArguments.RequireString(request.RelativePath, "relativePath", WorkspaceToolNames.FilesWrite).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.FilesWrite} requires a string relativePath.", isError: true));
        }

        var result = await _workspaceFileService.WriteFileAsync(
            request.RootName.Trim(),
            request.RelativePath.Trim(),
            request.Content ?? string.Empty,
            cancellationToken).ConfigureAwait(false);

        return result.Match<Fin<ToolCallResult>>(
            Succ: value =>
            {
                var structuredContent = JsonSerializer.SerializeToElement(value, WorkspaceJsonSerializerContext.Default.WorkspaceFileWriteResult);
                return Fin.Succ<ToolCallResult>(ToolCallResult.Text(
                    $"Wrote {value.BytesWritten} bytes to '{value.RelativePath}'.",
                    structuredContent: structuredContent));
            },
            Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
    }
}
