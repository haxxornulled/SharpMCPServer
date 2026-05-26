namespace MCPServer.Client.ConsoleApp;

internal static partial class McpClientConsole
{
    private static bool TryCreateOAuthScope(ConsoleOptions options, out McpClientConsoleOAuthScope? oauthScope)
    {
        oauthScope = null;
        if (options.Transport is not ConsoleTransportKind.Http || !options.UseOAuthInteractive)
        {
            return true;
        }

        var oauthAuthorizationScopeResult = McpClientConsoleOAuthComposition.CreateScope(options);
        if (oauthAuthorizationScopeResult.IsFail)
        {
            Console.Error.WriteLine(McpClientConsoleResultHelpers.GetError(oauthAuthorizationScopeResult));
            return false;
        }

        oauthScope = McpClientConsoleResultHelpers.GetValue(oauthAuthorizationScopeResult);
        return true;
    }
}
