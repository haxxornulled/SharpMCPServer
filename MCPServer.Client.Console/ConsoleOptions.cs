namespace MCPServer.Client.ConsoleApp;

internal sealed class ConsoleOptions
{
    public ConsoleTransportKind Transport { get; private init; }

    public string? ServerPath { get; private init; }

    public Uri? Endpoint { get; private init; }

    public string? WorkingDirectory { get; private init; }

    public string? ToolName { get; private init; }

    public string? ToolArgumentsJson { get; private init; }

    public IReadOnlyList<string> ServerArguments { get; private init; } = Array.Empty<string>();

    public string? Origin { get; private init; }

    public string? BearerToken { get; private init; }

    public bool UseOAuthInteractive { get; private init; }

    public string? OAuthClientName { get; private init; }

    public Uri? OAuthClientUri { get; private init; }

    public string? OAuthClientId { get; private init; }

    public Uri? OAuthClientIdMetadataDocumentUri { get; private init; }

    public Uri? OAuthRedirectUri { get; private init; }

    public string? OAuthCallbackPath { get; private init; }

    public bool OAuthUseDynamicClientRegistration { get; private init; } = true;

    public bool OpenServerEventStream { get; private init; }

    public bool ShowHelp { get; private init; }

    public static string HelpText => """
    Usage:
      MCPServer.Client.Console --server-path <command-or-path> [options]
      MCPServer.Client.Console --endpoint <http-or-https-mcp-endpoint> [options]

    Options:
      --transport <stdio|http>  Optional explicit transport mode.
      --server-path <path>      Path to MCPServer.Host executable for stdio mode.
      --endpoint <uri>          MCP Streamable HTTP endpoint for HTTP mode.
      --working-directory <dir> Optional server working directory for stdio mode.
      --tool <name>             Tool to call after initialization. Defaults to server.info.
      --arguments <json>        Tool arguments as a JSON object.
      --server-arg <value>      Additional argument passed to the server process. Repeatable.
      --origin <origin>         Override HTTP Origin header in HTTP mode.
      --bearer-token <token>    Send a bearer token for HTTP mode.
      --oauth-interactive       Enable the OAuth authorization-code flow in HTTP mode.
      --oauth-client-name <n>   Client name used for OAuth registration and UI labels.
      --oauth-client-uri <uri>   Client URI used during OAuth registration.
      --oauth-client-id <id>    Pre-registered OAuth client identifier.
      --oauth-client-id-metadata-document-uri <uri>  OAuth client ID metadata document URL.
      --oauth-redirect-uri <uri>  Fixed loopback redirect URI for OAuth.
      --oauth-callback-path <path>  Loopback callback path for OAuth.
      --no-dynamic-client-registration  Disable OAuth dynamic client registration.
      --open-server-event-stream  Open the MCP SSE stream in HTTP mode.
      --no-open-server-event-stream  Disable the MCP SSE stream in HTTP mode.
      --help                    Show this help.

    Examples:
      MCPServer.Client.Console --server-path dotnet --server-arg C:\Visual Studio Projects\MCPServer\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.dll --working-directory C:\Visual Studio Projects\MCPServer\MCPServer.Host\bin\Debug\net10.0
      MCPServer.Client.Console --server-path .\MCPServer.Host.exe --tool ssh.profiles.list --arguments {}
      MCPServer.Client.Console --endpoint http://127.0.0.1:3000/mcp/ --open-server-event-stream --tool server.info
      MCPServer.Client.Console --endpoint http://127.0.0.1:3000/mcp/ --oauth-interactive --oauth-client-id https://client.example.com/metadata.json --tool server.info
    """;

    public static ConsoleOptions Parse(string[] args)
    {
        string? transportRaw = null;
        string? serverPath = null;
        string? endpointRaw = null;
        string? workingDirectory = null;
        string? toolName = null;
        string? toolArgumentsJson = null;
        string? origin = null;
        string? bearerToken = null;
        string? oauthClientName = null;
        string? oauthClientUriRaw = null;
        string? oauthClientId = null;
        string? oauthClientIdMetadataDocumentUriRaw = null;
        string? oauthRedirectUriRaw = null;
        string? oauthCallbackPath = null;
        var serverArguments = new List<string>();
        bool? openServerEventStream = null;
        var useOAuthInteractive = false;
        var oauthUseDynamicClientRegistration = true;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help" or "-h" or "/?":
                    showHelp = true;
                    break;
                case "--transport":
                    transportRaw = ReadValue(args, ref i, arg);
                    break;
                case "--server-path":
                    serverPath = ReadValue(args, ref i, arg);
                    break;
                case "--endpoint":
                    endpointRaw = ReadValue(args, ref i, arg);
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
                case "--origin":
                    origin = ReadValue(args, ref i, arg);
                    break;
                case "--bearer-token":
                    bearerToken = ReadValue(args, ref i, arg);
                    break;
                case "--oauth-interactive":
                    useOAuthInteractive = true;
                    break;
                case "--oauth-client-name":
                    oauthClientName = ReadValue(args, ref i, arg);
                    break;
                case "--oauth-client-uri":
                    oauthClientUriRaw = ReadValue(args, ref i, arg);
                    break;
                case "--oauth-client-id":
                    oauthClientId = ReadValue(args, ref i, arg);
                    break;
                case "--oauth-client-id-metadata-document-uri":
                    oauthClientIdMetadataDocumentUriRaw = ReadValue(args, ref i, arg);
                    break;
                case "--oauth-redirect-uri":
                    oauthRedirectUriRaw = ReadValue(args, ref i, arg);
                    break;
                case "--oauth-callback-path":
                    oauthCallbackPath = ReadValue(args, ref i, arg);
                    break;
                case "--no-dynamic-client-registration":
                    oauthUseDynamicClientRegistration = false;
                    break;
                case "--open-server-event-stream":
                    openServerEventStream = true;
                    break;
                case "--no-open-server-event-stream":
                    openServerEventStream = false;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (showHelp)
        {
            return new ConsoleOptions
            {
                ShowHelp = true
            };
        }

        var transport = ParseTransportKind(transportRaw);
        var hasServerPath = !string.IsNullOrWhiteSpace(serverPath);
        var hasEndpoint = !string.IsNullOrWhiteSpace(endpointRaw);
        var hasOAuthOptions =
            useOAuthInteractive ||
            !string.IsNullOrWhiteSpace(oauthClientName) ||
            !string.IsNullOrWhiteSpace(oauthClientUriRaw) ||
            !string.IsNullOrWhiteSpace(oauthClientId) ||
            !string.IsNullOrWhiteSpace(oauthClientIdMetadataDocumentUriRaw) ||
            !string.IsNullOrWhiteSpace(oauthRedirectUriRaw) ||
            !string.IsNullOrWhiteSpace(oauthCallbackPath) ||
            !oauthUseDynamicClientRegistration;

        if (transport is null)
        {
            transport = hasEndpoint && !hasServerPath
                ? ConsoleTransportKind.Http
                : hasServerPath && !hasEndpoint
                    ? ConsoleTransportKind.Stdio
                    : throw new ArgumentException("Specify exactly one of --server-path or --endpoint, or add --transport to choose explicitly.");
        }

        if (transport is ConsoleTransportKind.Stdio && hasEndpoint)
        {
            throw new ArgumentException("--endpoint can only be used with HTTP transport.");
        }

        if (transport is ConsoleTransportKind.Http && hasServerPath)
        {
            throw new ArgumentException("--server-path can only be used with stdio transport.");
        }

        if (transport is ConsoleTransportKind.Stdio && string.IsNullOrWhiteSpace(serverPath))
        {
            throw new ArgumentException("--server-path is required for stdio transport.");
        }

        if (transport is ConsoleTransportKind.Http && string.IsNullOrWhiteSpace(endpointRaw))
        {
            throw new ArgumentException("--endpoint is required for HTTP transport.");
        }

        if (transport is ConsoleTransportKind.Stdio && !string.IsNullOrWhiteSpace(origin))
        {
            throw new ArgumentException("--origin can only be used with HTTP transport.");
        }

        if (transport is ConsoleTransportKind.Stdio && !string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new ArgumentException("--bearer-token can only be used with HTTP transport.");
        }

        if (transport is ConsoleTransportKind.Stdio && hasOAuthOptions)
        {
            throw new ArgumentException("OAuth options can only be used with HTTP transport.");
        }

        if (transport is ConsoleTransportKind.Stdio && openServerEventStream is true)
        {
            throw new ArgumentException("--open-server-event-stream can only be used with HTTP transport.");
        }

        if (transport is ConsoleTransportKind.Http && !string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("--working-directory can only be used with stdio transport.");
        }

        if (transport is ConsoleTransportKind.Http && serverArguments.Count > 0)
        {
            throw new ArgumentException("--server-arg can only be used with stdio transport.");
        }

        if (!string.IsNullOrWhiteSpace(bearerToken) && hasOAuthOptions)
        {
            throw new ArgumentException("--bearer-token cannot be combined with OAuth interactive options.");
        }

        if (transport is ConsoleTransportKind.Http && openServerEventStream is null)
        {
            openServerEventStream = true;
        }

        var endpoint = hasEndpoint
            ? CreateEndpoint(endpointRaw)
            : null;

        if (!string.IsNullOrWhiteSpace(oauthClientUriRaw))
        {
            oauthClientUriRaw = CreateAbsoluteUri(oauthClientUriRaw).AbsoluteUri;
        }

        var oauthClientUri = !string.IsNullOrWhiteSpace(oauthClientUriRaw)
            ? CreateAbsoluteUri(oauthClientUriRaw)
            : null;

        var oauthClientIdMetadataDocumentUri = !string.IsNullOrWhiteSpace(oauthClientIdMetadataDocumentUriRaw)
            ? CreateAbsoluteUri(oauthClientIdMetadataDocumentUriRaw)
            : null;

        var oauthRedirectUri = !string.IsNullOrWhiteSpace(oauthRedirectUriRaw)
            ? CreateAbsoluteUri(oauthRedirectUriRaw)
            : null;

        if (hasOAuthOptions)
        {
            useOAuthInteractive = true;
        }

        return new ConsoleOptions
        {
            Transport = transport.Value,
            ServerPath = serverPath,
            Endpoint = endpoint,
            WorkingDirectory = workingDirectory,
            ToolName = toolName,
            ToolArgumentsJson = toolArgumentsJson,
            ServerArguments = serverArguments,
            Origin = origin,
            BearerToken = bearerToken,
            UseOAuthInteractive = useOAuthInteractive,
            OAuthClientName = oauthClientName,
            OAuthClientUri = oauthClientUri,
            OAuthClientId = oauthClientId,
            OAuthClientIdMetadataDocumentUri = oauthClientIdMetadataDocumentUri,
            OAuthRedirectUri = oauthRedirectUri,
            OAuthCallbackPath = oauthCallbackPath,
            OAuthUseDynamicClientRegistration = oauthUseDynamicClientRegistration,
            OpenServerEventStream = openServerEventStream ?? false,
            ShowHelp = showHelp
        };
    }

    private static ConsoleTransportKind? ParseTransportKind(string? transportRaw)
    {
        if (string.IsNullOrWhiteSpace(transportRaw))
        {
            return null;
        }

        return transportRaw.Trim().ToLowerInvariant() switch
        {
            "stdio" => ConsoleTransportKind.Stdio,
            "http" => ConsoleTransportKind.Http,
            _ => throw new ArgumentException($"Unsupported transport '{transportRaw}'.")
        };
    }

    private static Uri CreateEndpoint(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
        {
            throw new ArgumentException($"--endpoint is not a valid absolute URI: {value}");
        }

        return endpoint;
    }

    private static Uri CreateAbsoluteUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            throw new ArgumentException($"The supplied URI is not valid: {value}");
        }

        return absoluteUri;
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

internal enum ConsoleTransportKind
{
    Stdio,
    Http
}
