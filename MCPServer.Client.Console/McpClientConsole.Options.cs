namespace MCPServer.Client.ConsoleApp;

internal static partial class McpClientConsole
{
    private static bool TryParseOptions(string[] args, out ConsoleOptions options)
    {
        try
        {
            options = ConsoleOptions.Parse(args);
            return true;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(ConsoleOptions.HelpText);
            options = null!;
            return false;
        }
    }
}
