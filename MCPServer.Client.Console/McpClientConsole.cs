using System.Text.Json;

namespace MCPServer.Client.ConsoleApp;

internal static partial class McpClientConsole
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (!TryParseOptions(args, out var options))
        {
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(ConsoleOptions.HelpText);
            return 0;
        }

        JsonElement? toolArguments = null;
        using var argumentsDocument = McpClientConsoleArgumentParser.ParseArguments(options.ToolArgumentsJson, out toolArguments, out var argumentError);
        if (!string.IsNullOrEmpty(argumentError))
        {
            Console.Error.WriteLine(argumentError);
            return 2;
        }

        return await RunConfiguredAsync(options, toolArguments, cancellationToken).ConfigureAwait(false);
    }
}
