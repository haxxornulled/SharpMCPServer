using LanguageExt;
using MCPServer.Ssh.Models;

namespace MCPServer.Ssh.Interfaces;

public interface ISshAgentRuntime
{
    ValueTask<Fin<SshAgentLaunchResponse>> LaunchAsync(SshAgentLaunchRequest request, CancellationToken cancellationToken);

    ValueTask<Fin<SshAgentStatusResponse>> GetStatusAsync(string agentId, CancellationToken cancellationToken);

    ValueTask<Fin<SshAgentOutputResponse>> GetOutputAsync(SshAgentOutputRequest request, CancellationToken cancellationToken);

    ValueTask<Fin<SshAgentCancelResponse>> CancelAsync(string agentId, CancellationToken cancellationToken);
}
