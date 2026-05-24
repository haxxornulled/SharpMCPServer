using LanguageExt;
using MCPServer.Tools.Ssh.Models;

namespace MCPServer.Tools.Ssh.Interfaces;

public interface ISshExecutionPolicy
{
    ValueTask<Fin<SshExecutionPolicyDecision>> EvaluateAsync(SshExecutionRequest request, CancellationToken cancellationToken);
}
