using LanguageExt;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleResultHelpers
{
    public static T GetValue<T>(Fin<T> value)
    {
        return value.Match(
            Succ: static result => result,
            Fail: static error => throw new InvalidOperationException(error.Message));
    }

    public static string GetError<T>(Fin<T> value)
    {
        return value.Match(
            Succ: static _ => string.Empty,
            Fail: static error => error.Message);
    }
}
