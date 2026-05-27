using System.Text.Json;
using MCPServer.Client.Interfaces;

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

        if (options.ChatMode)
        {
            if (!string.IsNullOrWhiteSpace(options.ToolArgumentsJson))
            {
                Console.Error.WriteLine("--chat cannot be combined with --arguments.");
                return 2;
            }

            return await RunChatConfiguredAsync(options, cancellationToken).ConfigureAwait(false);
        }

        var toolName = McpClientConsoleToolPresentation.GetToolName(options);

        JsonElement? toolArguments = null;
        using var argumentsDocument = McpClientConsoleArgumentParser.ParseArguments(options.ToolArgumentsJson, out toolArguments, out var argumentError);
        if (!string.IsNullOrEmpty(argumentError))
        {
            Console.Error.WriteLine(argumentError);
            return 2;
        }

        using var probeArgumentsDocument = McpClientConsoleArgumentParser.BuildProbeArguments(options, toolName, out var probeArguments, out var probeError);
        if (!string.IsNullOrEmpty(probeError))
        {
            Console.Error.WriteLine(probeError);
            return 2;
        }

        if (probeArguments is not null)
        {
            toolArguments = probeArguments;
        }

        using var generateArgumentsDocument = McpClientConsoleArgumentParser.BuildGenerateArguments(options, toolName, toolArguments, out var generateArguments, out var generateError);
        if (!string.IsNullOrEmpty(generateError))
        {
            Console.Error.WriteLine(generateError);
            return 2;
        }

        if (generateArguments is not null)
        {
            toolArguments = generateArguments;
        }

        return await RunConfiguredAsync(
            options,
            cancellationToken,
            session => McpClientConsoleSessionRunner.RunSessionAsync(session, options, toolArguments, cancellationToken)).ConfigureAwait(false);
    }
}
