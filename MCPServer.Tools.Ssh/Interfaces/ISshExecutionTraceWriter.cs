using LanguageExt;
using Unit = LanguageExt.Unit;
using MCPServer.Tools.Ssh.Models;

namespace MCPServer.Tools.Ssh.Interfaces;

public interface ISshExecutionTraceWriter
{
    ValueTask<Fin<Unit>> WriteAsync(SshExecutionResponse response, CancellationToken cancellationToken);
}
