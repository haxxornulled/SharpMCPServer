namespace MCPServer.Ssh.Interfaces;

public interface ISshPathResolver
{
    string ResolveConfiguredPath(string path);

    string ResolveContentPath(string relativePath);

    string ResolveUserDataPath(string relativePath);

    string ResolveLegacyRoamingUserDataPath(string relativePath);
}
