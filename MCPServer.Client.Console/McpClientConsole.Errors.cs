using LanguageExt;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleErrors
{
    public static int WriteFailure<T>(Fin<T> value)
    {
        var error = McpClientConsoleResultHelpers.GetError(value);
        Console.Error.WriteLine(error);
        McpClientConsoleOutput.WriteProviderStartHintIfNeeded(Console.Error, error);
        return 1;
    }
}
