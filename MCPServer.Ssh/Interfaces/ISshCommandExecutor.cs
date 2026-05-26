using LanguageExt;
using MCPServer.Ssh.Models;

namespace MCPServer.Ssh.Interfaces;

public interface ISshCommandExecutor
{
    ValueTask<Fin<SshCommandExecutionResult>> ExecuteAsync(SshExecutionCommand command, CancellationToken cancellationToken);
}
