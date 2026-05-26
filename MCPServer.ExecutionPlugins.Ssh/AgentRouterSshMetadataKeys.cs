namespace MCPServer.ExecutionPlugins.Ssh;

public static class AgentRouterSshMetadataKeys
{
    public const string Profile = "ssh.profile";
    public const string WorkingDirectory = "ssh.workingDirectory";
    public const string TimeoutSecondsPerStep = "ssh.timeoutSecondsPerStep";
    public const string Command = "ssh.command";
    public const string ArgumentsJson = "ssh.argumentsJson";
    public const string CommandsJson = "ssh.commandsJson";
}
