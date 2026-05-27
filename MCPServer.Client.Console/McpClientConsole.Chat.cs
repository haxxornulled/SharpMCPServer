using MCPServer.Client.Interfaces;

namespace MCPServer.Client.ConsoleApp;

internal static partial class McpClientConsole
{
    private static Task<int> RunChatConfiguredAsync(ConsoleOptions options, CancellationToken cancellationToken)
    {
        return RunConfiguredAsync(
            options,
            cancellationToken,
            session => McpClientConsoleChatRunner.RunAsync(
                session,
                options,
                Console.In,
                Console.Out,
                Console.Error,
                cancellationToken));
    }
}
