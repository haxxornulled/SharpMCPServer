using LanguageExt;
using Unit = LanguageExt.Unit;
using MCPServer.Ssh.Models;

namespace MCPServer.Ssh.Interfaces;

public interface ISshExecutionTraceWriter
{
    ValueTask<Fin<Unit>> WriteAsync(SshExecutionResponse response, CancellationToken cancellationToken);
}
