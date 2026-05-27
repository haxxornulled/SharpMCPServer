using System.Text.Json;
using MCPServer.Client.Interfaces;

namespace MCPServer.Client.ConsoleApp;

internal static partial class McpClientConsole
{
    private static async Task<int> RunConfiguredAsync(
        ConsoleOptions options,
        CancellationToken cancellationToken,
        Func<IMcpClientSession, Task<int>> sessionRunner)
    {
        McpClientConsoleOAuthScope? oauthScope = null;
        try
        {
            if (!TryCreateOAuthScope(options, out oauthScope))
            {
                return 1;
            }

            var sessionScopeResult = await McpClientConsoleSessionComposition.CreateScopeAsync(options, oauthScope?.Provider, cancellationToken).ConfigureAwait(false);
            if (sessionScopeResult.IsFail)
            {
                return McpClientConsoleErrors.WriteFailure(sessionScopeResult);
            }

            await using var sessionScope = McpClientConsoleResultHelpers.GetValue(sessionScopeResult);
            return await sessionRunner(sessionScope.Session).ConfigureAwait(false);
        }
        finally
        {
            if (oauthScope is not null)
            {
                await oauthScope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
