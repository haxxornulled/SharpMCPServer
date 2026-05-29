using MCPServer.ChatLauncher;
using Xunit;

namespace MCPServer.UnitTests.ChatLauncher;

public sealed class RepositoryLayoutTests
{
    [Fact]
    public void ResolveRepositoryRoot_FindsMarkerAboveLauncherOutputFolder()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"mcpserver-chat-launcher-{Guid.NewGuid():N}");
        var launcherOutputFolder = Path.Combine(repositoryRoot, "MCPServer.ChatLauncher", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(launcherOutputFolder);
        File.WriteAllText(Path.Combine(repositoryRoot, "MCPServer.slnx"), string.Empty);

        try
        {
            var resolvedRoot = RepositoryLayout.ResolveRepositoryRoot(launcherOutputFolder);

            Assert.Equal(repositoryRoot, resolvedRoot);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryParse_UsesRouterDefaultWhenNoneIsSpecified()
    {
        var parsed = ChatLauncherArguments.TryParse(Array.Empty<string>(), out var options, out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(string.Empty, options.Provider);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void TryParse_AllowsAnExplicitProviderOverride()
    {
        var parsed = ChatLauncherArguments.TryParse(["--provider", "ollama"], out var options, out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal("ollama", options.Provider);
        Assert.False(options.ShowHelp);
    }
}
