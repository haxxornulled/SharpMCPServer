using System.Text.Json;
using MCPServer.AgentRouter.IntegrationTests.Ssh.Tools.Testing;
using MCPServer.Domain.Mcp;
using MCPServer.Ssh.Configuration;
using Xunit;

namespace MCPServer.AgentRouter.IntegrationTests.Ssh.Tools;

public sealed class SshProfilesListToolTests
{
    [Fact]
    public async Task ExecuteAsync_Returns_Profile_DisplayName_In_Structured_Content()
    {
        var sut = new SshProfilesListToolSut()
            .AddProfile(new SshProfileDefinition
            {
                Name = "debian-root-lab",
                DisplayName = "Debian Root Lab",
                Host = "173.255.205.169",
                Port = 22,
                Username = "root",
                HostKeySha256 = "SHA256:Q7mMEDNG2w/v+PBa0ogNmW3ECGDGapU2NFgKRX5/5yI",
                AllowedCommands = ["whoami"],
                Source = "test"
            });

        var result = await sut.ExecuteAsync(TestContext.Current.CancellationToken);

        var profile = SshProfilesListToolSut.SingleProfile(result);
        Assert.Equal("debian-root-lab", profile.GetProperty("name").GetString());
        Assert.Equal("Debian Root Lab", profile.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_Falls_Back_To_Profile_Name_When_DisplayName_Is_Blank()
    {
        var sut = new SshProfilesListToolSut()
            .AddProfile(new SshProfileDefinition
            {
                Name = "dev",
                DisplayName = " ",
                Host = "192.0.2.10",
                Port = 22,
                Username = "james",
                Source = "test"
            });

        var result = await sut.ExecuteAsync(TestContext.Current.CancellationToken);

        var profile = SshProfilesListToolSut.SingleProfile(result);
        Assert.Equal("dev", profile.GetProperty("name").GetString());
        Assert.Equal("dev", profile.GetProperty("displayName").GetString());
    }


    [Fact]
    public async Task ExecuteAsync_Returns_Credential_Diagnostics_For_Configured_Password_Credential_Reference()
    {
        const string credentialReference = "ssh/profile/debian-root-lab/password";

        var sut = new SshProfilesListToolSut()
            .AddProfile(new SshProfileDefinition
            {
                Name = "debian-root-lab",
                DisplayName = "Debian Root Lab",
                Host = "173.255.205.169",
                Port = 22,
                Username = "root",
                PasswordCredentialReference = credentialReference,
                HostKeySha256 = "SHA256:Q7mMEDNG2w/v+PBa0ogNmW3ECGDGapU2NFgKRX5/5yI",
                AllowAllCommands = true,
                Privileged = true,
                AllowedRoot = true,
                Source = "test"
            });

        var result = await sut.ExecuteAsync(TestContext.Current.CancellationToken);

        var profile = SshProfilesListToolSut.SingleProfile(result);
        Assert.Equal("password-credential-reference", profile.GetProperty("credentialKind").GetString());
        Assert.True(profile.GetProperty("hasCredentialConfigured").GetBoolean());
        Assert.False(profile.GetProperty("passwordCredentialReferenceSet").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Password_Credential_Available_When_Resolver_Can_Resolve_It()
    {
        const string credentialReference = "ssh/profile/debian-root-lab/password";

        var sut = new SshProfilesListToolSut()
            .AddAvailableCredential(credentialReference)
            .AddProfile(new SshProfileDefinition
            {
                Name = "debian-root-lab",
                Host = "173.255.205.169",
                Port = 22,
                Username = "root",
                PasswordCredentialReference = credentialReference,
                HostKeySha256 = "SHA256:Q7mMEDNG2w/v+PBa0ogNmW3ECGDGapU2NFgKRX5/5yI",
                Source = "test"
            });

        var result = await sut.ExecuteAsync(TestContext.Current.CancellationToken);

        var profile = SshProfilesListToolSut.SingleProfile(result);
        Assert.Equal("password-credential-reference", profile.GetProperty("credentialKind").GetString());
        Assert.True(profile.GetProperty("hasCredentialConfigured").GetBoolean());
        Assert.True(profile.GetProperty("passwordCredentialReferenceSet").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_Returns_No_Credential_Diagnostics_When_Profile_Has_No_Credential_Source()
    {
        var sut = new SshProfilesListToolSut()
            .AddProfile(new SshProfileDefinition
            {
                Name = "dev",
                Host = "192.0.2.10",
                Port = 22,
                Username = "james",
                Source = "test"
            });

        var result = await sut.ExecuteAsync(TestContext.Current.CancellationToken);

        var profile = SshProfilesListToolSut.SingleProfile(result);
        Assert.Equal("none", profile.GetProperty("credentialKind").GetString());
        Assert.False(profile.GetProperty("hasCredentialConfigured").GetBoolean());
        Assert.False(profile.GetProperty("passwordCredentialReferenceSet").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_Rejects_Arguments()
    {
        var sut = new SshProfilesListToolSut();
        using var arguments = JsonDocument.Parse("""
        {
          "ignored": true
        }
        """);

        var result = await sut.ExecuteAsync(arguments.RootElement, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var text = Assert.Single(result.Content);
        var content = Assert.IsType<TextToolContent>(text);
        Assert.Equal("ssh.profiles.list does not accept arguments.", content.Text);
    }
}
