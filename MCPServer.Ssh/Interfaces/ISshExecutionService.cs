using LanguageExt;
using MCPServer.Ssh.Models;

namespace MCPServer.Ssh.Interfaces;

public interface ISshExecutionService
{
    ValueTask<Fin<SshExecutionResponse>> ExecuteAsync(SshExecutionRequest request, CancellationToken cancellationToken);
}
