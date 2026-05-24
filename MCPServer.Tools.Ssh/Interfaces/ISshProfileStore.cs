using LanguageExt;
using MCPServer.Tools.Ssh.Configuration;

namespace MCPServer.Tools.Ssh.Interfaces;

public interface ISshProfileStore
{
    ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(CancellationToken cancellationToken);
}
