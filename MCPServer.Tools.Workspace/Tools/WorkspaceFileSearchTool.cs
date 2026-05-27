using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Workspace;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Tools.Workspace.Tools;

public sealed class WorkspaceFileSearchTool : IMcpTool
{
    private static readonly JsonElement InputSchema = WorkspaceToolSchemas.CreateFileSearchInputSchema();
    private static readonly JsonElement OutputSchema = WorkspaceToolSchemas.CreateFileSearchOutputSchema();

    private readonly IWorkspaceFileService _workspaceFileService;

    public WorkspaceFileSearchTool(IWorkspaceFileService workspaceFileService)
    {
        _workspaceFileService = workspaceFileService ?? throw new ArgumentNullException(nameof(workspaceFileService));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = WorkspaceToolNames.FilesSearch,
        Title = "Search Workspace Files",
        Description = "Searches text files inside configured workspace roots.",
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

        var requestResult = WorkspaceToolArguments.Parse(arguments, WorkspaceJsonSerializerContext.Default.WorkspaceFileSearchRequest, WorkspaceToolNames.FilesSearch);
        if (requestResult.IsFail)
        {
            var error = requestResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workspace argument parse success while handling failure."),
                Fail: static value => value);
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true));
        }

        var request = requestResult.Match(
            Succ: static value => value,
            Fail: error => throw new InvalidOperationException($"Unexpected workspace file search argument parse failure: {error.Message}"));
        if (WorkspaceToolArguments.RequireString(request.Query, "query", WorkspaceToolNames.FilesSearch).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.FilesSearch} requires a string query.", isError: true));
        }

        var caseSensitive = request.CaseSensitive;
        var result = await _workspaceFileService.SearchAsync(
            string.IsNullOrWhiteSpace(request.RootName) ? null : request.RootName.Trim(),
            request.Query.Trim(),
            caseSensitive,
            cancellationToken).ConfigureAwait(false);

        return result.Match<Fin<ToolCallResult>>(
            Succ: value =>
            {
                var structuredContent = JsonSerializer.SerializeToElement(value, WorkspaceJsonSerializerContext.Default.WorkspaceFileSearchResult);
                var text = value.HitCount == 1
                    ? "1 search hit found."
                    : $"{value.HitCount} search hits found across {value.FilesScanned} files.";
                if (value.Truncated)
                {
                    text += " Results were truncated.";
                }

                return Fin.Succ<ToolCallResult>(ToolCallResult.Text(text, structuredContent: structuredContent));
            },
            Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
    }
}
