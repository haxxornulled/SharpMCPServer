using System.Text.Json;
using LanguageExt;
using MCPServer.Client;
using MCPServer.Client.Stdio;
using MCPServer.Domain.Mcp;

return await McpClientConsole.RunAsync(args, CancellationToken.None).ConfigureAwait(false);

internal static class McpClientConsole
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = ConsoleOptions.Parse(args);
        if (options.ShowHelp)
        {
            Console.WriteLine(ConsoleOptions.HelpText);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.ServerPath))
        {
            Console.Error.WriteLine("--server-path is required. Build MCPServer.Host first, then pass the path to MCPServer.Host.exe.");
            Console.Error.WriteLine(ConsoleOptions.HelpText);
            return 2;
        }

        JsonElement? toolArguments = null;
        using var argumentsDocument = ParseArguments(options.ToolArgumentsJson, out toolArguments, out var argumentError);
        if (!string.IsNullOrEmpty(argumentError))
        {
            Console.Error.WriteLine(argumentError);
            return 2;
        }

        var processOptions = new McpClientProcessOptions
        {
            ServerExecutablePath = options.ServerPath,
            ServerArguments = options.ServerArguments,
            WorkingDirectory = options.WorkingDirectory,
            ClientName = "mcpserver-client-console",
            ClientTitle = "MCP Server Client Console",
            ClientVersion = "1.0.0"
        };

        var started = await StdioMcpClientSession.StartAsync(processOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (started.IsFail)
        {
            Console.Error.WriteLine(GetError(started));
            return 1;
        }

        await using var session = GetValue(started);

        var initialized = await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (initialized.IsFail)
        {
            Console.Error.WriteLine(GetError(initialized));
            return 1;
        }

        var initializeResult = GetValue(initialized);
        Console.WriteLine($"Connected to {initializeResult.ServerInfo.Name} {initializeResult.ServerInfo.Version} using MCP {initializeResult.ProtocolVersion}.");

        var tools = await session.ListToolsAsync(cursor: null, cancellationToken).ConfigureAwait(false);
        if (tools.IsFail)
        {
            Console.Error.WriteLine(GetError(tools));
            return 1;
        }

        var toolList = GetValue(tools);
        Console.WriteLine("Tools exposed by server:");
        foreach (var tool in toolList.Tools)
        {
            Console.WriteLine($"- {tool.Name}: {tool.Description}");
        }

        var toolName = string.IsNullOrWhiteSpace(options.ToolName) ? "server.info" : options.ToolName;
        Console.WriteLine();
        Console.WriteLine($"Calling tool: {toolName}");

        var callResult = await session.CallToolAsync(toolName, toolArguments, cancellationToken).ConfigureAwait(false);
        if (callResult.IsFail)
        {
            Console.Error.WriteLine(GetError(callResult));
            return 1;
        }

        PrintToolResult(GetValue(callResult));
        return 0;
    }

    private static JsonDocument? ParseArguments(string? json, out JsonElement? arguments, out string? error)
    {
        arguments = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                error = "--arguments must be a JSON object.";
                return null;
            }

            arguments = document.RootElement.Clone();
            return document;
        }
        catch (JsonException ex)
        {
            error = $"--arguments is not valid JSON: {ex.Message}";
            return null;
        }
    }

    private static void PrintToolResult(ToolCallResult result)
    {
        Console.WriteLine(result.IsError ? "Tool returned an error result." : "Tool returned a success result.");
        foreach (var content in result.Content)
        {
            if (content is TextToolContent text)
            {
                Console.WriteLine(text.Text);
            }
        }

        if (result.StructuredContent is { } structuredContent)
        {
            Console.WriteLine();
            Console.WriteLine("Structured content:");
            Console.WriteLine(JsonSerializer.Serialize(structuredContent, McpJsonSerializerContext.Default.JsonElement));
        }
    }

    private static T GetValue<T>(Fin<T> value)
    {
        return value.Match(
            Succ: static result => result,
            Fail: static error => throw new InvalidOperationException(error.Message));
    }

    private static string GetError<T>(Fin<T> value)
    {
        return value.Match(
            Succ: static _ => string.Empty,
            Fail: static error => error.Message);
    }
}

internal sealed class ConsoleOptions
{
    public string? ServerPath { get; private init; }

    public string? WorkingDirectory { get; private init; }

    public string? ToolName { get; private init; }

    public string? ToolArgumentsJson { get; private init; }

    public IReadOnlyList<string> ServerArguments { get; private init; } = Array.Empty<string>();

    public bool ShowHelp { get; private init; }

    public static string HelpText => """
    Usage:
      MCPServer.Client.Console --server-path <path-to-MCPServer.Host.exe> [options]

    Options:
      --server-path <path>      Required. Path to MCPServer.Host executable.
      --working-directory <dir> Optional server working directory.
      --tool <name>             Tool to call after initialization. Defaults to server.info.
      --arguments <json>        Tool arguments as a JSON object.
      --server-arg <value>      Additional argument passed to the server process. Repeatable.
      --help                    Show this help.

    Examples:
      MCPServer.Client.Console --server-path C:\Visual Studio Projects\MCPServer\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe
      MCPServer.Client.Console --server-path .\MCPServer.Host.exe --tool ssh.profiles.list --arguments {}
    """;

    public static ConsoleOptions Parse(string[] args)
    {
        string? serverPath = null;
        string? workingDirectory = null;
        string? toolName = null;
        string? toolArgumentsJson = null;
        var serverArguments = new List<string>();
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help" or "-h" or "/?":
                    showHelp = true;
                    break;
                case "--server-path":
                    serverPath = ReadValue(args, ref i, arg);
                    break;
                case "--working-directory":
                    workingDirectory = ReadValue(args, ref i, arg);
                    break;
                case "--tool":
                    toolName = ReadValue(args, ref i, arg);
                    break;
                case "--arguments":
                    toolArgumentsJson = ReadValue(args, ref i, arg);
                    break;
                case "--server-arg":
                    serverArguments.Add(ReadValue(args, ref i, arg));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new ConsoleOptions
        {
            ServerPath = serverPath,
            WorkingDirectory = workingDirectory,
            ToolName = toolName,
            ToolArgumentsJson = toolArgumentsJson,
            ServerArguments = serverArguments,
            ShowHelp = showHelp
        };
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }
}
