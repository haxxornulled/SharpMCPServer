using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Workspace;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Tools.Workspace.Tools;

public sealed class WorkspaceFileApplyPatchTool : IMcpTool
{
    private static readonly JsonElement InputSchema = WorkspaceToolSchemas.CreateFilePatchInputSchema();
    private static readonly JsonElement OutputSchema = WorkspaceToolSchemas.CreateFilePatchOutputSchema();

    private readonly IWorkspaceFileService _workspaceFileService;

    public WorkspaceFileApplyPatchTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService ?? throw new ArgumentNullException(nameof(workspaceFileService));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = WorkspaceToolNames.FilesApplyPatch,
        Title = "Apply Workspace Patch",
        Description = "Applies a unified diff patch to a workspace-scoped text file.",
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

        var requestResult = WorkspaceToolArguments.Parse(arguments, WorkspaceJsonSerializerContext.Default.WorkspacePatchRequest, WorkspaceToolNames.FilesApplyPatch);
        if (requestResult.IsFail)
        {
            var error = requestResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workspace argument parse success while handling failure."),
                Fail: static value => value);
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true));
        }

        var request = requestResult.Match(
            Succ: static value => value,
            Fail: error => throw new InvalidOperationException($"Unexpected workspace patch argument parse failure: {error.Message}"));
        if (WorkspaceToolArguments.RequireString(request.RootName, "rootName", WorkspaceToolNames.FilesApplyPatch).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.FilesApplyPatch} requires a string rootName.", isError: true));
        }

        if (WorkspaceToolArguments.RequireString(request.RelativePath, "relativePath", WorkspaceToolNames.FilesApplyPatch).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.FilesApplyPatch} requires a string relativePath.", isError: true));
        }

        if (WorkspaceToolArguments.RequireString(request.Patch, "patch", WorkspaceToolNames.FilesApplyPatch).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.FilesApplyPatch} requires a string patch.", isError: true));
        }

        var result = await _workspaceFileService.ApplyPatchAsync(
            request.RootName.Trim(),
            request.RelativePath.Trim(),
            request.Patch,
            cancellationToken).ConfigureAwait(false);

        return result.Match<Fin<ToolCallResult>>(
            Succ: value =>
            {
                var structuredContent = JsonSerializer.SerializeToElement(value, WorkspaceJsonSerializerContext.Default.WorkspacePatchResult);
                return Fin.Succ<ToolCallResult>(ToolCallResult.Text(
                    $"Applied {value.AppliedHunks} patch hunk(s) to '{value.RelativePath}'.",
                    structuredContent: structuredContent));
            },
            Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
    }
}
