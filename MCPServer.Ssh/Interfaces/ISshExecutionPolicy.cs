using LanguageExt;
using MCPServer.Ssh.Models;

namespace MCPServer.Ssh.Interfaces;

public interface ISshExecutionPolicy
{
    ValueTask<Fin<SshExecutionPolicyDecision>> EvaluateAsync(SshExecutionRequest request, CancellationToken cancellationToken);
}
