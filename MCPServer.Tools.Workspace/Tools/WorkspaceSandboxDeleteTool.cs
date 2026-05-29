using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Tools.Workspace.Tools;

public sealed class WorkspaceSandboxDeleteTool : IMcpTool
{
    private static readonly JsonElement InputSchema = WorkspaceToolSchemas.CreateSandboxDeleteInputSchema();
    private static readonly JsonElement OutputSchema = WorkspaceToolSchemas.CreateSandboxDeleteOutputSchema();

    private readonly IWorkspaceSandboxManager _sandboxManager;
    private readonly McpWorkspaceOptions _options;

    public WorkspaceSandboxDeleteTool(IWorkspaceSandboxManager sandboxManager, McpWorkspaceOptions options)
    {
        _sandboxManager = sandboxManager ?? throw new ArgumentNullException(nameof(sandboxManager));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = WorkspaceToolNames.SandboxesDelete,
        Title = "Delete Workspace Sandbox",
        Description = "Deletes an isolated workspace sandbox and removes it from the active root list.",
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

        var requestResult = WorkspaceToolArguments.Parse(arguments, WorkspaceJsonSerializerContext.Default.WorkspaceSandboxDeleteRequest, WorkspaceToolNames.SandboxesDelete);
        if (requestResult.IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text(
                requestResult.Match(
                    Succ: static _ => throw new InvalidOperationException("Unexpected workspace argument parse success while handling failure."),
                    Fail: static error => error).Message,
                isError: true));
        }

        var request = requestResult.Match(
            Succ: static value => value,
            Fail: error => throw new InvalidOperationException($"Unexpected workspace sandbox delete argument parse failure: {error.Message}"));

        if (WorkspaceToolArguments.RequireString(request.SandboxName, "sandboxName", WorkspaceToolNames.SandboxesDelete).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.SandboxesDelete} requires a string sandboxName.", isError: true));
        }

        var result = await _sandboxManager.DeleteAsync(
            request.SandboxName.Trim(),
            _options.ApprovalToken,
            cancellationToken).ConfigureAwait(false);

        return result.Match<Fin<ToolCallResult>>(
            Succ: value =>
            {
                var structuredContent = JsonSerializer.SerializeToElement(
                    new WorkspaceSandboxDeleteResult
                    {
                        SandboxName = value.Name,
                        Path = value.Path,
                        Deleted = !value.Exists,
                        SourceRootName = value.SourceRootName,
                        CreatedUtc = value.CreatedUtc
                    },
                    WorkspaceJsonSerializerContext.Default.WorkspaceSandboxDeleteResult);
                return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"Deleted workspace sandbox '{value.Name}'.", structuredContent: structuredContent));
            },
            Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
    }
}
