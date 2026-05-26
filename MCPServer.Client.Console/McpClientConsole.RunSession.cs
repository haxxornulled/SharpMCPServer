using System.Text.Json;
using MCPServer.Client.Interfaces;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleSessionRunner
{
    public static async Task<int> RunSessionAsync(IMcpClientSession session, ConsoleOptions options, JsonElement? toolArguments, CancellationToken cancellationToken)
    {
        var initialized = await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (initialized.IsFail)
        {
            return McpClientConsoleErrors.WriteFailure(initialized);
        }

        McpClientConsoleStatusOutput.PrintConnectedMessage(McpClientConsoleResultHelpers.GetValue(initialized));

        var tools = await session.ListToolsAsync(cursor: null, cancellationToken).ConfigureAwait(false);
        if (tools.IsFail)
        {
            return McpClientConsoleErrors.WriteFailure(tools);
        }

        McpClientConsoleToolPresentation.PrintTools(McpClientConsoleResultHelpers.GetValue(tools));

        var toolName = McpClientConsoleToolPresentation.GetToolName(options);
        McpClientConsoleStatusOutput.PrintCallingTool(toolName);

        var callResult = await session.CallToolAsync(toolName, toolArguments, cancellationToken).ConfigureAwait(false);
        if (callResult.IsFail)
        {
            return McpClientConsoleErrors.WriteFailure(callResult);
        }

        McpClientConsoleOutput.PrintToolResult(McpClientConsoleResultHelpers.GetValue(callResult));
        return 0;
    }
}
