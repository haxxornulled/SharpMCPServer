using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Tools.Workspace.Tools;

public sealed class WorkspaceSandboxCreateTool : IMcpTool
{
    private static readonly JsonElement InputSchema = WorkspaceToolSchemas.CreateSandboxCreateInputSchema();
    private static readonly JsonElement OutputSchema = WorkspaceToolSchemas.CreateSandboxCreateOutputSchema();

    private readonly IWorkspaceSandboxManager _sandboxManager;
    private readonly McpWorkspaceOptions _options;

    public WorkspaceSandboxCreateTool(IWorkspaceSandboxManager sandboxManager, McpWorkspaceOptions options)
    {
        _sandboxManager = sandboxManager ?? throw new ArgumentNullException(nameof(sandboxManager));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = WorkspaceToolNames.SandboxesCreate,
        Title = "Create Workspace Sandbox",
        Description = "Creates an isolated writable sandbox by copying a configured root or existing sandbox.",
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

        var requestResult = WorkspaceToolArguments.Parse(arguments, WorkspaceJsonSerializerContext.Default.WorkspaceSandboxCreateRequest, WorkspaceToolNames.SandboxesCreate);
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
            Fail: error => throw new InvalidOperationException($"Unexpected workspace sandbox create argument parse failure: {error.Message}"));

        if (WorkspaceToolArguments.RequireString(request.SourceRootName, "sourceRootName", WorkspaceToolNames.SandboxesCreate).IsFail)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{WorkspaceToolNames.SandboxesCreate} requires a string sourceRootName.", isError: true));
        }

        var result = await _sandboxManager.CreateAsync(
            request.SourceRootName.Trim(),
            string.IsNullOrWhiteSpace(request.SandboxName) ? null : request.SandboxName.Trim(),
            _options.ApprovalToken,
            cancellationToken).ConfigureAwait(false);

        return result.Match<Fin<ToolCallResult>>(
            Succ: value =>
            {
                var structuredContent = JsonSerializer.SerializeToElement(
                    new WorkspaceSandboxCreateResult { Sandbox = value },
                    WorkspaceJsonSerializerContext.Default.WorkspaceSandboxCreateResult);
                return Fin.Succ<ToolCallResult>(ToolCallResult.Text($"Created workspace sandbox '{value.Name}'.", structuredContent: structuredContent));
            },
            Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
    }
}
