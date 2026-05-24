using LanguageExt;
using MCPServer.Tools.Ssh.Models;

namespace MCPServer.Tools.Ssh.Interfaces;

public interface ISshCommandExecutor
{
    ValueTask<Fin<SshCommandExecutionResult>> ExecuteAsync(SshExecutionCommand command, CancellationToken cancellationToken);
}
