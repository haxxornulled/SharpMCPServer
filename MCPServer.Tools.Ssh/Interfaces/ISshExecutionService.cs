using LanguageExt;
using MCPServer.Tools.Ssh.Models;

namespace MCPServer.Tools.Ssh.Interfaces;

public interface ISshExecutionService
{
    ValueTask<Fin<SshExecutionResponse>> ExecuteAsync(SshExecutionRequest request, CancellationToken cancellationToken);
}
