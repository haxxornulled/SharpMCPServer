using LanguageExt;
using MCPServer.Ssh.Configuration;

namespace MCPServer.Ssh.Interfaces;

public interface ISshProfileStore
{
    ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(CancellationToken cancellationToken);
}
