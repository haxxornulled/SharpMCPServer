using LanguageExt;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleErrors
{
    public static int WriteFailure<T>(Fin<T> value)
    {
        Console.Error.WriteLine(McpClientConsoleResultHelpers.GetError(value));
        return 1;
    }
}
