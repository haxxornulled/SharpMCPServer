using System.Text.Json;

namespace MCPServer.Client.ConsoleApp;

internal static partial class McpClientConsole
{
    private static async Task<int> RunConfiguredAsync(ConsoleOptions options, JsonElement? toolArguments, CancellationToken cancellationToken)
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
            return await McpClientConsoleSessionRunner.RunSessionAsync(sessionScope.Session, options, toolArguments, cancellationToken).ConfigureAwait(false);
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
