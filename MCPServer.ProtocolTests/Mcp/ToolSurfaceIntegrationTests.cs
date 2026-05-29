using System.Globalization;
using System.Text.Json;
using Autofac;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.Application.Mcp;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.Tools;
using MCPServer.Domain.Mcp;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;
using MCPServer.Tools.Inference;
using MCPServer.Tools.Ssh;
using MCPServer.Tools.Ssh.Tools;
using MCPServer.Tools.Workspace;
using MCPServer.Workspace.Configuration;
using Xunit;

namespace MCPServer.ProtocolTests.Mcp;

public sealed class ToolSurfaceIntegrationTests
{
    [Fact]
    public async Task Tools_List_Exposes_All_Registered_Tools()
    {
        using var workspace = new WorkspaceTestFixture();
        var dependencies = new ToolDependencies();
        using var harness = CreateHarness(workspace, dependencies);

        await InitializeSessionAsync(harness);

        using var transcript = await harness.SendAsync(ToolsListFrame(2));

        Assert.Equal(1, transcript.Count);
        AssertResponseId(transcript[0], 2);
        Assert.False(transcript[0].TryGetProperty("error", out _));

        var tools = transcript[0].GetProperty("result").GetProperty("tools");
        var toolNames = GetToolNames(tools);

        Assert.Equal(25, toolNames.Count);
        Assert.True(toolNames.SetEquals(new[]
        {
            McpToolNames.ServerInfo,
            McpToolNames.ClientSample,
            McpToolNames.ClientElicitForm,
            McpToolNames.ClientElicitUrl,
            AgentToolNames.Create,
            AgentToolNames.SubagentCreate,
            AgentToolNames.Status,
            AgentToolNames.Approve,
            AgentToolNames.Cancel,
            InferenceToolNames.Generate,
            InferenceToolNames.ProvidersList,
            WorkspaceToolNames.RootsList,
            WorkspaceToolNames.SandboxesList,
            WorkspaceToolNames.SandboxesCreate,
            WorkspaceToolNames.SandboxesDelete,
            WorkspaceToolNames.FilesRead,
            WorkspaceToolNames.FilesSearch,
            WorkspaceToolNames.FilesWrite,
            WorkspaceToolNames.FilesApplyPatch,
            SshToolNames.Exec,
            SshToolNames.ProfilesList,
            SshToolNames.AgentLaunch,
            SshToolNames.AgentStatus,
            SshToolNames.AgentOutput,
            SshToolNames.AgentCancel
        }));

        Assert.Equal("forbidden", FindTool(tools, McpToolNames.ServerInfo).GetProperty("execution").GetProperty("taskSupport").GetString());
        Assert.Equal("optional", FindTool(tools, McpToolNames.ClientSample).GetProperty("execution").GetProperty("taskSupport").GetString());
        Assert.Equal("forbidden", FindTool(tools, WorkspaceToolNames.FilesWrite).GetProperty("execution").GetProperty("taskSupport").GetString());
    }

    [Fact]
    public async Task Server_Info_Tool_RoundTrip_Returns_Runtime_Metadata()
    {
        using var workspace = new WorkspaceTestFixture();
        var dependencies = new ToolDependencies();
        using var harness = CreateHarness(workspace, dependencies);

        await InitializeSessionAsync(harness);

        var result = await CallToolAsync(harness, 2, McpToolNames.ServerInfo, "{}");

        Assert.Equal("MCPServer", result.GetProperty("structuredContent").GetProperty("name").GetString());
        Assert.Equal("2025-11-25", result.GetProperty("structuredContent").GetProperty("protocolVersion").GetString());
        Assert.Contains("tools/list", GetStringArray(result.GetProperty("structuredContent").GetProperty("capabilities")));
        Assert.Contains(McpToolNames.ClientSample, GetStringArray(result.GetProperty("structuredContent").GetProperty("capabilities")));
        Assert.Contains("client.elicit.url", GetStringArray(result.GetProperty("structuredContent").GetProperty("capabilities")));
        Assert.Contains(AgentToolNames.Create, GetStringArray(result.GetProperty("structuredContent").GetProperty("capabilities")));
        Assert.Contains(AgentToolNames.SubagentCreate, GetStringArray(result.GetProperty("structuredContent").GetProperty("capabilities")));
        Assert.Contains(AgentToolNames.Approve, GetStringArray(result.GetProperty("structuredContent").GetProperty("capabilities")));
        var contentText = Assert.Single(result.GetProperty("content").EnumerateArray()).GetProperty("text").GetString();
        Assert.Contains("MCPServer", contentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_Feature_Tools_RoundTrip_Through_Client_Feature_Invoker()
    {
        using var workspace = new WorkspaceTestFixture();
        var dependencies = new ToolDependencies();
        using var harness = CreateHarness(workspace, dependencies);

        await InitializeSessionAsync(harness);

        var sample = await CallToolAsync(harness, 2, McpToolNames.ClientSample, """
        {"prompt":"hello","systemPrompt":"You are concise.","maxTokens":64,"temperature":0.25,"task":true}
        """);

        Assert.NotNull(dependencies.ClientFeatures.LastCreateMessageRequest);
        Assert.NotNull(dependencies.ClientFeatures.LastCreateMessageRequest!.Task);
        Assert.Single(dependencies.ClientFeatures.LastCreateMessageRequest.Messages);
        Assert.Equal("user", dependencies.ClientFeatures.LastCreateMessageRequest.Messages[0].Role);
        Assert.Equal("hello", dependencies.ClientFeatures.LastCreateMessageRequest.Messages[0].Content.GetString());
        Assert.Equal("You are concise.", dependencies.ClientFeatures.LastCreateMessageRequest.SystemPrompt);
        Assert.Equal(64, dependencies.ClientFeatures.LastCreateMessageRequest.MaxTokens);
        Assert.Equal(0.25, dependencies.ClientFeatures.LastCreateMessageRequest.Temperature);
        Assert.Equal("sample-model", sample.GetProperty("structuredContent").GetProperty("model").GetString());
        Assert.Equal("assistant", sample.GetProperty("structuredContent").GetProperty("role").GetString());

        var form = await CallToolAsync(harness, 3, McpToolNames.ClientElicitForm, """
        {"message":"confirm changes","requestedSchema":{"type":"object","properties":{"answer":{"type":"string"}}},"task":true}
        """);

        Assert.NotNull(dependencies.ClientFeatures.LastElicitFormRequest);
        Assert.NotNull(dependencies.ClientFeatures.LastElicitFormRequest!.Task);
        Assert.Equal("confirm changes", dependencies.ClientFeatures.LastElicitFormRequest.Message);
        Assert.Equal(JsonValueKind.Object, dependencies.ClientFeatures.LastElicitFormRequest.RequestedSchema.ValueKind);
        Assert.Equal("accept", form.GetProperty("structuredContent").GetProperty("action").GetString());

        var url = await CallToolAsync(harness, 4, McpToolNames.ClientElicitUrl, """
        {"message":"open the docs","elicitationId":"doc-1","url":"https://example.com/docs","task":true}
        """);

        Assert.NotNull(dependencies.ClientFeatures.LastElicitUrlRequest);
        Assert.NotNull(dependencies.ClientFeatures.LastElicitUrlRequest!.Task);
        Assert.Equal("open the docs", dependencies.ClientFeatures.LastElicitUrlRequest.Message);
        Assert.Equal("doc-1", dependencies.ClientFeatures.LastElicitUrlRequest.ElicitationId);
        Assert.Equal("https://example.com/docs", dependencies.ClientFeatures.LastElicitUrlRequest.Url);
        Assert.Equal("accept", url.GetProperty("structuredContent").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Inference_Tools_RoundTrip_Through_Router_And_Providers()
    {
        using var workspace = new WorkspaceTestFixture();
        var dependencies = new ToolDependencies();
        using var harness = CreateHarness(workspace, dependencies);

        await InitializeSessionAsync(harness);

        var generate = await CallToolAsync(harness, 2, InferenceToolNames.Generate, """
        {"prompt":"Explain the repo bubble.","systemPrompt":"You are precise.","providerId":"lmstudio","fallbackProviderIds":["ollama","anthropic"],"strategy":"FanOutCompare","model":"local-model","maxTokens":128,"temperature":0.2}
        """);

        Assert.NotNull(dependencies.InferenceRouter.LastRequest);
        Assert.Equal(2, dependencies.InferenceRouter.LastRequest!.Messages.Count);
        Assert.Equal(InferenceRole.System, dependencies.InferenceRouter.LastRequest.Messages[0].Role);
        Assert.Equal("You are precise.", dependencies.InferenceRouter.LastRequest.Messages[0].Content);
        Assert.Equal(InferenceRole.User, dependencies.InferenceRouter.LastRequest.Messages[1].Role);
        Assert.Equal("Explain the repo bubble.", dependencies.InferenceRouter.LastRequest.Messages[1].Content);
        Assert.NotNull(dependencies.InferenceRouter.LastRequest.RoutingHint);
        Assert.Equal(InferenceRoutingStrategy.FanOutCompare, dependencies.InferenceRouter.LastRequest.RoutingHint!.Strategy);
        Assert.Equal("lmstudio", dependencies.InferenceRouter.LastRequest.RoutingHint.PreferredProviderId);
        Assert.Equal("local-model", dependencies.InferenceRouter.LastRequest.Model);
        Assert.Equal(128, dependencies.InferenceRouter.LastRequest.MaxTokens);
        Assert.Equal(0.2, dependencies.InferenceRouter.LastRequest.Temperature);
        Assert.Equal("lmstudio", generate.GetProperty("structuredContent").GetProperty("providerId").GetString());
        Assert.Equal(46, generate.GetProperty("structuredContent").GetProperty("usage").GetProperty("totalTokens").GetInt32());

        var staticProviders = await CallToolAsync(harness, 3, InferenceToolNames.ProvidersList, "{}");
        Assert.False(staticProviders.GetProperty("structuredContent").GetProperty("probed").GetBoolean());
        var providers = staticProviders.GetProperty("structuredContent").GetProperty("providers");
        Assert.Equal(2, providers.GetArrayLength());
        Assert.Equal("anthropic", providers[0].GetProperty("providerId").GetString());
        Assert.Equal("disabled", providers[0].GetProperty("status").GetString());
        Assert.Equal("lmstudio", providers[1].GetProperty("providerId").GetString());
        Assert.Equal("ready", providers[1].GetProperty("status").GetString());

        var probedProviders = await CallToolAsync(harness, 4, InferenceToolNames.ProvidersList, """
        {"probe":true,"probeTimeoutMilliseconds":50}
        """);

        Assert.True(probedProviders.GetProperty("structuredContent").GetProperty("probed").GetBoolean());
        var probed = probedProviders.GetProperty("structuredContent").GetProperty("providers");
        Assert.Equal(2, probed.GetArrayLength());
        Assert.Equal("anthropic", probed[0].GetProperty("providerId").GetString());
        Assert.Equal("disabled", probed[0].GetProperty("status").GetString());
        Assert.Equal("lmstudio", probed[1].GetProperty("providerId").GetString());
        Assert.Equal("ready", probed[1].GetProperty("status").GetString());
        Assert.Equal(0, dependencies.InferenceClients[0].ProbeCallCount);
        Assert.Equal(1, dependencies.InferenceClients[1].ProbeCallCount);
    }

    [Fact]
    public async Task Workspace_Tools_RoundTrip_Through_Real_Workspace_Services()
    {
        using var workspace = new WorkspaceTestFixture();
        var dependencies = new ToolDependencies();
        using var harness = CreateHarness(workspace, dependencies);

        await InitializeSessionAsync(harness);

        var roots = await CallToolAsync(harness, 2, WorkspaceToolNames.RootsList, "{}");
        var rootEntries = roots.GetProperty("structuredContent").GetProperty("roots");
        Assert.Single(rootEntries.EnumerateArray());
        var root = Assert.Single(rootEntries.EnumerateArray());
        Assert.Equal("workspace", root.GetProperty("name").GetString());
        Assert.Equal(workspace.RootPath, root.GetProperty("path").GetString());
        Assert.True(root.GetProperty("allowWrite").GetBoolean());

        var read = await CallToolAsync(harness, 3, WorkspaceToolNames.FilesRead, """
        {"rootName":"workspace","relativePath":"src/Program.cs"}
        """);
        Assert.Contains("Console.WriteLine", read.GetProperty("structuredContent").GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Equal(3, read.GetProperty("structuredContent").GetProperty("lineCount").GetInt32());

        var search = await CallToolAsync(harness, 4, WorkspaceToolNames.FilesSearch, """
        {"rootName":"workspace","query":"Console.WriteLine","caseSensitive":false}
        """);
        Assert.Equal(1, search.GetProperty("structuredContent").GetProperty("hitCount").GetInt32());
        Assert.Equal(1, search.GetProperty("structuredContent").GetProperty("hits").GetArrayLength());

        var write = await CallToolAsync(harness, 5, WorkspaceToolNames.FilesWrite, """
        {"rootName":"workspace","relativePath":"src/Written.cs","content":"namespace Demo;\n"}
        """);
        Assert.Equal(2, write.GetProperty("structuredContent").GetProperty("lineCount").GetInt32());
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "src", "Written.cs")));

        var patch = await CallToolAsync(harness, 6, WorkspaceToolNames.FilesApplyPatch, """
        {"rootName":"workspace","relativePath":"src/Program.cs","patch":"diff --git a/src/Program.cs b/src/Program.cs\n--- a/src/Program.cs\n+++ b/src/Program.cs\n@@ -1,2 +1,2 @@\n-using System;\n+using System;\n-Console.WriteLine(\"Hello\");\n+Console.WriteLine(\"Hello, workspace!\");\n","message":"Update Program.cs hello-world output."}
        """);
        Assert.Equal(1, patch.GetProperty("structuredContent").GetProperty("appliedHunks").GetInt32());
        Assert.Equal(2, patch.GetProperty("structuredContent").GetProperty("addedLines").GetInt32());
        Assert.Equal(2, patch.GetProperty("structuredContent").GetProperty("removedLines").GetInt32());
        Assert.Equal("Update Program.cs hello-world output.", patch.GetProperty("structuredContent").GetProperty("message").GetString());
        Assert.Contains("Hello, workspace!", await File.ReadAllTextAsync(Path.Combine(workspace.RootPath, "src", "Program.cs"), CancellationToken.None), StringComparison.Ordinal);

        var sandboxesBeforeCreate = await CallToolAsync(harness, 7, WorkspaceToolNames.SandboxesList, "{}");
        Assert.Empty(sandboxesBeforeCreate.GetProperty("structuredContent").GetProperty("sandboxes").EnumerateArray());

        var create = await CallToolAsync(harness, 8, WorkspaceToolNames.SandboxesCreate, """
        {"sourceRootName":"workspace","sandboxName":"sandbox-one"}
        """);
        var sandbox = create.GetProperty("structuredContent").GetProperty("sandbox");
        Assert.Equal("sandbox-one", sandbox.GetProperty("name").GetString());
        Assert.Equal("workspace", sandbox.GetProperty("sourceRootName").GetString());
        Assert.True(sandbox.GetProperty("exists").GetBoolean());
        Assert.Equal("sandbox", sandbox.GetProperty("kind").GetString());
        Assert.True(Directory.Exists(sandbox.GetProperty("path").GetString()));

        var sandboxRead = await CallToolAsync(harness, 9, WorkspaceToolNames.FilesRead, """
        {"rootName":"sandbox-one","relativePath":"src/Program.cs"}
        """);
        Assert.Contains("Hello, workspace!", sandboxRead.GetProperty("structuredContent").GetProperty("content").GetString(), StringComparison.Ordinal);

        var sandboxesAfterCreate = await CallToolAsync(harness, 10, WorkspaceToolNames.SandboxesList, "{}");
        var sandboxEntries = sandboxesAfterCreate.GetProperty("structuredContent").GetProperty("sandboxes");
        Assert.Single(sandboxEntries.EnumerateArray());
        Assert.Equal("sandbox-one", Assert.Single(sandboxEntries.EnumerateArray()).GetProperty("name").GetString());

        var delete = await CallToolAsync(harness, 11, WorkspaceToolNames.SandboxesDelete, """
        {"sandboxName":"sandbox-one"}
        """);
        Assert.True(delete.GetProperty("structuredContent").GetProperty("deleted").GetBoolean());

        var sandboxesAfterDelete = await CallToolAsync(harness, 12, WorkspaceToolNames.SandboxesList, "{}");
        Assert.Empty(sandboxesAfterDelete.GetProperty("structuredContent").GetProperty("sandboxes").EnumerateArray());
    }

    [Fact]
    public async Task Ssh_Tools_RoundTrip_Through_Fake_Ssh_Backends()
    {
        using var workspace = new WorkspaceTestFixture();
        var dependencies = new ToolDependencies();
        using var harness = CreateHarness(workspace, dependencies);

        await InitializeSessionAsync(harness);

        var profiles = await CallToolAsync(harness, 2, SshToolNames.ProfilesList, "{}");
        var profileEntries = profiles.GetProperty("structuredContent").GetProperty("profiles");
        Assert.Equal(2, profileEntries.GetArrayLength());
        Assert.Equal("alpha", profileEntries[0].GetProperty("name").GetString());
        Assert.Equal("runner", profileEntries[1].GetProperty("name").GetString());
        Assert.Equal("password-credential-reference", profileEntries[1].GetProperty("credentialKind").GetString());
        Assert.True(profileEntries[1].GetProperty("passwordCredentialReferenceSet").GetBoolean());
        var sources = profiles.GetProperty("structuredContent").GetProperty("sources");
        Assert.Single(sources.EnumerateArray());
        Assert.Equal(2, Assert.Single(sources.EnumerateArray()).GetProperty("profileCount").GetInt32());

        var exec = await CallToolAsync(harness, 3, SshToolNames.Exec, """
        {"profile":"runner","command":"echo","arguments":["hello"],"workingDirectory":"/tmp/project","timeoutSeconds":30,"operationKey":"op-1"}
        """);
        Assert.NotNull(dependencies.SshExecutionService.LastRequest);
        Assert.Equal("runner", dependencies.SshExecutionService.LastRequest!.Profile);
        Assert.Equal("echo", dependencies.SshExecutionService.LastRequest.Command);
        Assert.Single(dependencies.SshExecutionService.LastRequest.Arguments);
        Assert.Equal("hello", dependencies.SshExecutionService.LastRequest.Arguments[0]);
        Assert.Equal("/tmp/project", dependencies.SshExecutionService.LastRequest.WorkingDirectory);
        Assert.Equal(30, dependencies.SshExecutionService.LastRequest.TimeoutSeconds);
        Assert.Equal("op-1", dependencies.SshExecutionService.LastRequest.OperationKey);
        Assert.Equal("succeeded", exec.GetProperty("structuredContent").GetProperty("status").GetString());
        Assert.Equal("SSH command completed.", Assert.Single(exec.GetProperty("content").EnumerateArray()).GetProperty("text").GetString());

        var launch = await CallToolAsync(harness, 4, SshToolNames.AgentLaunch, """
        {"profile":"runner","objective":"verify connectivity","workingDirectory":"/tmp/project","timeoutSecondsPerStep":45,"operationKey":"agent-op","commands":[{"command":"whoami"},{"command":"pwd","workingDirectory":"/tmp/project"}]}
        """);
        Assert.NotNull(dependencies.SshAgentRuntime.LastLaunchRequest);
        Assert.Equal("runner", dependencies.SshAgentRuntime.LastLaunchRequest!.Profile);
        Assert.Equal("verify connectivity", dependencies.SshAgentRuntime.LastLaunchRequest.Objective);
        Assert.Equal(2, dependencies.SshAgentRuntime.LastLaunchRequest.Commands.Count);
        Assert.Equal("queued", launch.GetProperty("structuredContent").GetProperty("status").GetString());
        Assert.Equal(2, launch.GetProperty("structuredContent").GetProperty("commandCount").GetInt32());

        var status = await CallToolAsync(harness, 5, SshToolNames.AgentStatus, """
        {"agentId":"agent-1"}
        """);
        Assert.Equal("agent-1", dependencies.SshAgentRuntime.LastStatusAgentId);
        Assert.Equal("working", status.GetProperty("structuredContent").GetProperty("status").GetString());
        Assert.Equal(1, status.GetProperty("structuredContent").GetProperty("steps").GetArrayLength());

        var output = await CallToolAsync(harness, 6, SshToolNames.AgentOutput, """
        {"agentId":"agent-1","stdoutOffset":5,"stderrOffset":3,"maxChars":1000}
        """);
        Assert.NotNull(dependencies.SshAgentRuntime.LastOutputRequest);
        Assert.Equal("agent-1", dependencies.SshAgentRuntime.LastOutputRequest!.AgentId);
        Assert.Equal(5, dependencies.SshAgentRuntime.LastOutputRequest.StdoutOffset);
        Assert.Equal(3, dependencies.SshAgentRuntime.LastOutputRequest.StderrOffset);
        Assert.Equal(1000, dependencies.SshAgentRuntime.LastOutputRequest.MaxChars);
        Assert.Equal("stdout chunk", output.GetProperty("structuredContent").GetProperty("stdout").GetString());
        Assert.Equal("stderr chunk", output.GetProperty("structuredContent").GetProperty("stderr").GetString());

        var cancel = await CallToolAsync(harness, 7, SshToolNames.AgentCancel, """
        {"agentId":"agent-1"}
        """);
        Assert.Equal("agent-1", dependencies.SshAgentRuntime.LastCancelAgentId);
        Assert.True(cancel.GetProperty("structuredContent").GetProperty("cancellationRequested").GetBoolean());
        Assert.Equal("cancelled", cancel.GetProperty("structuredContent").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Agent_Tools_RoundTrip_Through_Agent_Coordinator()
    {
        using var workspace = new WorkspaceTestFixture();
        var dependencies = new ToolDependencies();
        using var harness = CreateHarness(workspace, dependencies);

        await InitializeSessionAsync(harness);

        var create = await CallToolAsync(harness, 12, AgentToolNames.Create, """
        {"objective":"Coordinate the repo cleanup","capability":"planning.context","workflowMode":"deterministic","routeTarget":"local"}
        """);

        var createContent = create.GetProperty("structuredContent");
        var parentRunId = createContent.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(parentRunId));
        Assert.Equal("agent", createContent.GetProperty("kind").GetString());
        Assert.Equal("queued", createContent.GetProperty("status").GetString());
        Assert.Equal("Coordinate the repo cleanup", createContent.GetProperty("objective").GetString());
        Assert.Equal("planning.context", createContent.GetProperty("capability").GetString());
        Assert.Equal("deterministic", createContent.GetProperty("workflowMode").GetString());
        Assert.Equal("local", createContent.GetProperty("routeTarget").GetString());
        Assert.Equal("agent", createContent.GetProperty("metadata").GetProperty("agent.kind").GetString());
        Assert.Equal("planning.context", createContent.GetProperty("metadata").GetProperty("agent.capability").GetString());
        Assert.Equal("deterministic", createContent.GetProperty("metadata").GetProperty("agent.workflowMode").GetString());
        Assert.Equal("local", createContent.GetProperty("metadata").GetProperty("agent.routeTarget").GetString());
        Assert.Equal("agent", dependencies.AgentRunCoordinator.LastStartRequest?.MetadataOrEmpty[AgentRouterMetadataKeys.Kind]);
        Assert.Equal("planning.context", dependencies.AgentRunCoordinator.LastStartRequest?.MetadataOrEmpty[AgentRouterMetadataKeys.Capability]);

        var subagent = await CallToolAsync(harness, 13, AgentToolNames.SubagentCreate, $$"""
        {"objective":"Verify the execution edge","capability":"remote-shell","parentRunId":{{QuoteJsonString(parentRunId!)}},"workflowMode":"agentic","routeTarget":"remote"}
        """);

        var subagentContent = subagent.GetProperty("structuredContent");
        var subagentRunId = subagentContent.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(subagentRunId));
        Assert.Equal("subagent", subagentContent.GetProperty("kind").GetString());
        Assert.Equal(parentRunId, subagentContent.GetProperty("parentRunId").GetString());
        Assert.Equal("remote-shell", subagentContent.GetProperty("capability").GetString());
        Assert.Equal("agentic", subagentContent.GetProperty("workflowMode").GetString());
        Assert.Equal("remote", subagentContent.GetProperty("routeTarget").GetString());
        Assert.Equal(parentRunId, subagentContent.GetProperty("metadata").GetProperty("agent.parentRunId").GetString());
        Assert.Equal("subagent", dependencies.AgentRunCoordinator.LastStartRequest?.MetadataOrEmpty[AgentRouterMetadataKeys.Kind]);
        Assert.Equal(parentRunId, dependencies.AgentRunCoordinator.LastStartRequest?.MetadataOrEmpty[AgentRouterMetadataKeys.ParentRunId]);

        var status = await CallToolAsync(harness, 14, AgentToolNames.Status, $$"""
        {"runId":{{QuoteJsonString(parentRunId!)}}}
        """);

        var statusContent = status.GetProperty("structuredContent");
        Assert.Equal(parentRunId, statusContent.GetProperty("runId").GetString());
        Assert.Equal("queued", statusContent.GetProperty("status").GetString());
        Assert.Equal("Coordinate the repo cleanup", statusContent.GetProperty("objective").GetString());
        Assert.Equal("planning.context", statusContent.GetProperty("capability").GetString());
        Assert.Equal("agent", statusContent.GetProperty("kind").GetString());

        var approve = await CallToolAsync(harness, 15, AgentToolNames.Approve, $$"""
        {"runId":{{QuoteJsonString(parentRunId!)}},"approvalId":"approval-1","approvedBy":"protocol-tests"}
        """);

        var approveContent = approve.GetProperty("structuredContent");
        Assert.Equal(parentRunId, approveContent.GetProperty("runId").GetString());
        Assert.True(approveContent.GetProperty("approvalGranted").GetBoolean());
        Assert.Equal("approval-1", approveContent.GetProperty("approvalId").GetString());
        Assert.Equal("protocol-tests", approveContent.GetProperty("approvedBy").GetString());
        Assert.Equal("queued", approveContent.GetProperty("status").GetString());
        Assert.Equal("approval-1", dependencies.AgentRunCoordinator.LastApproveRequest?.ApprovalId);
        Assert.Equal("protocol-tests", dependencies.AgentRunCoordinator.LastApproveRequest?.ApprovedBy);

        var cancel = await CallToolAsync(harness, 16, AgentToolNames.Cancel, $$"""
        {"runId":{{QuoteJsonString(subagentRunId!)}}}
        """);

        var cancelContent = cancel.GetProperty("structuredContent");
        Assert.Equal(subagentRunId, cancelContent.GetProperty("runId").GetString());
        Assert.Equal("cancelled", cancelContent.GetProperty("status").GetString());
        Assert.Equal("subagent", cancelContent.GetProperty("kind").GetString());
        Assert.Equal(subagentRunId, dependencies.AgentRunCoordinator.LastCancelRunId?.Value);
    }

    private static ProtocolTranscriptHarness CreateHarness(WorkspaceTestFixture workspace, ToolDependencies dependencies)
    {
        return ProtocolTranscriptHarness.Create(builder =>
        {
            builder.RegisterInstance(workspace.Options).AsSelf().SingleInstance();
            builder.RegisterModule(new WorkspaceToolsModule());
            builder.RegisterModule(new InferenceToolsModule());
            builder.RegisterModule(new SshToolsModule());
            builder.RegisterModule(new AgentRouterToolsModule());

            builder.RegisterInstance(dependencies.ClientFeatures).As<IMcpClientFeatureInvoker>().SingleInstance();

            builder.RegisterInstance(dependencies.InferenceRouter).As<IInferenceRouter>().SingleInstance();
            foreach (var client in dependencies.InferenceClients)
            {
                builder.RegisterInstance(client).As<IInferenceClient>().SingleInstance();
            }

            builder.RegisterInstance(dependencies.SshProfileStore).As<ISshProfileStore>().SingleInstance();
            builder.RegisterInstance(dependencies.SshCredentialResolver).As<ISshCredentialResolver>().SingleInstance();
            builder.RegisterInstance(dependencies.SshExecutionService).As<ISshExecutionService>().SingleInstance();
            builder.RegisterInstance(dependencies.SshAgentRuntime).As<ISshAgentRuntime>().SingleInstance();
            builder.RegisterInstance(dependencies.AgentRunCoordinator).As<IAgentRunCoordinator>().SingleInstance();
        });
    }

    private static async Task InitializeSessionAsync(ProtocolTranscriptHarness harness)
    {
        using var transcript = await harness.SendAsync(InitializeFrame(1), InitializedNotification());
        Assert.Equal(1, transcript.Count);
        AssertResponseId(transcript[0], 1);
        Assert.False(transcript[0].TryGetProperty("error", out _));
    }

    private static async Task<JsonElement> CallToolAsync(ProtocolTranscriptHarness harness, int requestId, string name, string argumentsJson)
    {
        using var transcript = await harness.SendAsync(ToolCallFrame(requestId, name, argumentsJson));
        Assert.Equal(1, transcript.Count);
        AssertResponseId(transcript[0], requestId);
        Assert.False(transcript[0].TryGetProperty("error", out _));

        var result = transcript[0].GetProperty("result").Clone();
        Assert.False(result.GetProperty("isError").GetBoolean());
        return result;
    }

    private static string InitializeFrame(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + ",\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"tool-integration\",\"version\":\"1\"}}}";
    }

    private static string InitializedNotification()
    {
        return """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
    }

    private static string ToolsListFrame(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + ",\"method\":\"tools/list\"}";
    }

    private static string ToolCallFrame(int id, string name, string argumentsJson)
    {
        return "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + ",\"method\":\"tools/call\",\"params\":{\"name\":" + QuoteJsonString(name) + ",\"arguments\":" + argumentsJson + "}}";
    }

    private static void AssertResponseId(JsonElement response, int expectedId)
    {
        Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
        Assert.Equal(expectedId, response.GetProperty("id").GetInt32());
    }

    private static System.Collections.Generic.HashSet<string> GetToolNames(JsonElement tools)
    {
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools.EnumerateArray())
        {
            names.Add(tool.GetProperty("name").GetString() ?? string.Empty);
        }

        return names;
    }

    private static JsonElement FindTool(JsonElement tools, string name)
    {
        foreach (var tool in tools.EnumerateArray())
        {
            if (string.Equals(tool.GetProperty("name").GetString(), name, StringComparison.Ordinal))
            {
                return tool.Clone();
            }
        }

        throw new InvalidOperationException($"Tool '{name}' was not found in the tool list.");
    }

    private static string[] GetStringArray(JsonElement array)
    {
        var values = new string[array.GetArrayLength()];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            values[index++] = item.GetString() ?? string.Empty;
        }

        return values;
    }

    private static string QuoteJsonString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static JsonElement CreateJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class WorkspaceTestFixture : IDisposable
    {
        private readonly string _basePath;

        public WorkspaceTestFixture()
        {
            _basePath = Path.Combine(Path.GetTempPath(), "MCPServer.ProtocolTests.Workspace", Guid.NewGuid().ToString("N"));
            RootPath = Path.Combine(_basePath, "workspace-root");
            SandboxBasePath = Path.Combine(_basePath, "sandboxes");
            DatabasePath = Path.Combine(_basePath, "workspace.db");

            Directory.CreateDirectory(Path.Combine(RootPath, "src"));
            File.WriteAllText(Path.Combine(RootPath, "src", "Program.cs"), "using System;\nConsole.WriteLine(\"Hello\");\n");

            Options = new McpWorkspaceOptions
            {
                ApprovalToken = "approved",
                SandboxBasePath = SandboxBasePath,
                Sqlite =
                {
                    DatabasePath = DatabasePath,
                    EnsureCreatedOnUse = true
                },
                Roots =
                [
                    new McpWorkspaceRootOptions
                    {
                        Name = "workspace",
                        Path = RootPath,
                        AllowWrite = true
                    }
                ]
            };

            Options.Validate();
        }

        public McpWorkspaceOptions Options { get; }

        public string RootPath { get; }

        public string SandboxBasePath { get; }

        public string DatabasePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_basePath))
            {
                Directory.Delete(_basePath, recursive: true);
            }
        }
    }

    private sealed class ToolDependencies
    {
        public ToolDependencies()
        {
            ClientFeatures = new FakeClientFeatureInvoker();
            InferenceRouter = new FakeInferenceRouter();
            InferenceClients =
            [
                new FakeInferenceClient(
                    "anthropic",
                    "Anthropic",
                    enabled: false,
                    probeResult: InferenceProviderProbeResult.Disabled("anthropic", "Anthropic")),
                new FakeInferenceClient(
                    "lmstudio",
                    "LM Studio",
                    enabled: true,
                    probeResult: InferenceProviderProbeResult.Ready("lmstudio", "LM Studio", 200, 17, "http://127.0.0.1:1234/v1/models"))
            ];

            SshCredentialResolver = new FakeSshCredentialResolver();
            SshProfileStore = new FakeSshProfileStore();
            SshExecutionService = new FakeSshExecutionService();
            SshAgentRuntime = new FakeSshAgentRuntime();
            AgentRunCoordinator = new FakeAgentRunCoordinator();

            ClientFeatures.CreateMessageResponse = CreateJsonElement("""
            {"model":"sample-model","role":"assistant","content":{"type":"text","text":"sample reply"},"stopReason":"stop"}
            """);
            ClientFeatures.ElicitFormResponse = CreateJsonElement("""
            {"action":"accept","content":{"answer":"yes"}}
            """);
            ClientFeatures.ElicitUrlResponse = CreateJsonElement("""
            {"action":"accept","content":{"opened":true}}
            """);

            InferenceRouter.Response = new InferenceResponse(
                "lmstudio",
                "local-model",
                "response text",
                "stop",
                new InferenceUsage(12, 34, 46),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["providerId"] = "lmstudio"
                });

            SshCredentialResolver.AddAvailableCredential("ssh/profile/runner/password");

            SshProfileStore.AddSource(new SshProfileSourceStatus
            {
                Path = "profiles.json",
                Exists = true,
                ProfileCount = 2
            });

            SshProfileStore.AddProfile(new SshProfileDefinition
            {
                Name = "alpha",
                DisplayName = "Alpha Profile",
                Host = "192.0.2.10",
                Port = 22,
                Username = "alice",
                Source = "test"
            });

            SshProfileStore.AddProfile(new SshProfileDefinition
            {
                Name = "runner",
                DisplayName = "Runner Profile",
                Host = "192.0.2.20",
                Port = 2222,
                Username = "root",
                PasswordCredentialReference = "ssh/profile/runner/password",
                HostKeySha256 = "SHA256:example",
                AllowedCommands = ["whoami"],
                AllowedRemotePathPrefixes = ["/srv"],
                AllowAllCommands = true,
                Privileged = true,
                AllowedRoot = true,
                Source = "test"
            });

            SshExecutionService.Response = new SshExecutionResponse
            {
                Id = "exec-1",
                Status = SshExecutionStatusNames.Succeeded,
                Allowed = true,
                PolicyDecision = "allow",
                Profile = "runner",
                Command = "echo",
                Arguments = ["hello"],
                WorkingDirectory = "/tmp/project",
                ExitCode = 0,
                TimedOut = false,
                Stdout = "stdout chunk",
                Stderr = "stderr chunk",
                StdoutTruncated = false,
                StderrTruncated = false,
                ElapsedMilliseconds = 12,
                Summary = "SSH command completed.",
                TraceId = "trace-1",
                CreatedAt = FixedTimestamp,
                CompletedAt = FixedTimestamp.AddSeconds(1)
            };

            SshAgentRuntime.LaunchResponse = new SshAgentLaunchResponse
            {
                AgentId = "agent-1",
                Status = SshAgentStatusNames.Queued,
                Profile = "runner",
                Objective = "verify connectivity",
                CommandCount = 2,
                CurrentStep = 0,
                PollIntervalMilliseconds = 500,
                CreatedAt = FixedTimestamp,
                LastUpdatedAt = FixedTimestamp,
                Summary = "SSH agent queued."
            };

            SshAgentRuntime.StatusResponse = new SshAgentStatusResponse
            {
                AgentId = "agent-1",
                Status = SshAgentStatusNames.Working,
                Profile = "runner",
                Objective = "verify connectivity",
                CommandCount = 2,
                CurrentStep = 1,
                CompletedSteps = 1,
                FailedSteps = 0,
                CancellationRequested = false,
                CurrentCommand = "whoami",
                Summary = "SSH agent is running.",
                StdoutTail = "stdout tail",
                StderrTail = "stderr tail",
                StdoutLength = 42,
                StderrLength = 12,
                Steps =
                [
                    new SshAgentStepSnapshot
                    {
                        Index = 0,
                        Status = SshAgentStatusNames.Completed,
                        Command = "whoami",
                        Arguments = [],
                        ExitCode = 0,
                        Summary = "first step completed",
                        StartedAt = FixedTimestamp,
                        CompletedAt = FixedTimestamp.AddSeconds(1)
                    }
                ],
                CreatedAt = FixedTimestamp,
                LastUpdatedAt = FixedTimestamp.AddSeconds(1),
                CompletedAt = null
            };

            SshAgentRuntime.OutputResponse = new SshAgentOutputResponse
            {
                AgentId = "agent-1",
                Status = SshAgentStatusNames.Working,
                Stdout = "stdout chunk",
                Stderr = "stderr chunk",
                StdoutOffset = 5,
                StderrOffset = 3,
                NextStdoutOffset = 17,
                NextStderrOffset = 16,
                StdoutTruncated = false,
                StderrTruncated = false
            };

            SshAgentRuntime.CancelResponse = new SshAgentCancelResponse
            {
                AgentId = "agent-1",
                Status = SshAgentStatusNames.Cancelled,
                CancellationRequested = true,
                Summary = "SSH agent cancellation requested.",
                LastUpdatedAt = FixedTimestamp.AddSeconds(2)
            };
        }

        public FakeClientFeatureInvoker ClientFeatures { get; }

        public FakeInferenceRouter InferenceRouter { get; }

        public IReadOnlyList<FakeInferenceClient> InferenceClients { get; }

        public FakeSshProfileStore SshProfileStore { get; }

        public FakeSshCredentialResolver SshCredentialResolver { get; }

        public FakeSshExecutionService SshExecutionService { get; }

        public FakeSshAgentRuntime SshAgentRuntime { get; }

        public FakeAgentRunCoordinator AgentRunCoordinator { get; }
    }

    private sealed class FakeClientFeatureInvoker : IMcpClientFeatureInvoker
    {
        public JsonElement CreateMessageResponse { get; set; } = CreateJsonElement("""
        {"model":"sample-model","role":"assistant","content":{"type":"text","text":"sample reply"},"stopReason":"stop"}
        """);

        public JsonElement ElicitFormResponse { get; set; } = CreateJsonElement("""
        {"action":"accept","content":{"answer":"yes"}}
        """);

        public JsonElement ElicitUrlResponse { get; set; } = CreateJsonElement("""
        {"action":"accept","content":{"opened":true}}
        """);

        public CreateMessageRequestParams? LastCreateMessageRequest { get; private set; }

        public ElicitRequestFormParams? LastElicitFormRequest { get; private set; }

        public ElicitRequestUrlParams? LastElicitUrlRequest { get; private set; }

        public ValueTask<Fin<JsonElement>> CreateMessageAsync(CreateMessageRequestParams parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCreateMessageRequest = parameters;
            return new ValueTask<Fin<JsonElement>>(Fin.Succ(CreateMessageResponse.Clone()));
        }

        public ValueTask<Fin<JsonElement>> ElicitFormAsync(ElicitRequestFormParams parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastElicitFormRequest = parameters;
            return new ValueTask<Fin<JsonElement>>(Fin.Succ(ElicitFormResponse.Clone()));
        }

        public ValueTask<Fin<JsonElement>> ElicitUrlAsync(ElicitRequestUrlParams parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastElicitUrlRequest = parameters;
            return new ValueTask<Fin<JsonElement>>(Fin.Succ(ElicitUrlResponse.Clone()));
        }
    }

    private sealed class FakeInferenceRouter : IInferenceRouter
    {
        public InferenceRequest? LastRequest { get; private set; }

        public InferenceResponse Response { get; set; } = new(
            "lmstudio",
            "local-model",
            "response text",
            "stop",
            new InferenceUsage(12, 34, 46),
            new Dictionary<string, string>(StringComparer.Ordinal));

        public ValueTask<Fin<InferenceResponse>> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return new ValueTask<Fin<InferenceResponse>>(Fin.Succ(Response));
        }
    }

    private sealed class FakeInferenceClient : IInferenceClient
    {
        private readonly InferenceProviderProbeResult _probeResult;

        public FakeInferenceClient(
            string providerId,
            string displayName,
            bool enabled,
            InferenceProviderProbeResult probeResult)
        {
            ProviderId = providerId;
            Descriptor = new InferenceProviderDescriptor(providerId, displayName, enabled, SupportsStreaming: true);
            _probeResult = probeResult;
        }

        public string ProviderId { get; }

        public InferenceProviderDescriptor Descriptor { get; }

        public int ProbeCallCount { get; private set; }

        public ValueTask<InferenceProviderProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCallCount++;
            return new ValueTask<InferenceProviderProbeResult>(_probeResult);
        }

        public ValueTask<Fin<InferenceResponse>> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<InferenceResponse>>(Fin.Fail<InferenceResponse>(Error.New("GenerateAsync is not part of this integration path.")));
        }
    }

    private sealed class FakeSshProfileStore : ISshProfileStore
    {
        private readonly Dictionary<string, SshProfileDefinition> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SshProfileSourceStatus> _sources = [];

        public void AddProfile(SshProfileDefinition profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                throw new ArgumentException("Profile name is required.", nameof(profile));
            }

            _profiles[profile.Name] = profile;
        }

        public void AddSource(SshProfileSourceStatus source)
        {
            ArgumentNullException.ThrowIfNull(source);
            _sources.Add(source);
        }

        public ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<SshProfileCatalog>>(Fin.Succ(new SshProfileCatalog
            {
                Profiles = new Dictionary<string, SshProfileDefinition>(_profiles, StringComparer.OrdinalIgnoreCase),
                Sources = _sources.ToArray()
            }));
        }
    }

    private sealed class FakeSshCredentialResolver : ISshCredentialResolver
    {
        private readonly System.Collections.Generic.HashSet<string> _availableCredentials = new(StringComparer.OrdinalIgnoreCase);

        public void AddAvailableCredential(string credentialReference)
        {
            if (!string.IsNullOrWhiteSpace(credentialReference))
            {
                _availableCredentials.Add(credentialReference);
            }
        }

        public ValueTask<string?> ResolveSecretAsync(string? credentialReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<string?>(HasCredential(credentialReference) ? "available-test-secret" : null);
        }

        public ValueTask<bool> HasSecretAsync(string? credentialReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<bool>(HasCredential(credentialReference));
        }

        private bool HasCredential(string? credentialReference)
        {
            return credentialReference is { Length: > 0 } reference && _availableCredentials.Contains(reference);
        }
    }

    private sealed class FakeSshExecutionService : ISshExecutionService
    {
        public SshExecutionRequest? LastRequest { get; private set; }

        public SshExecutionResponse Response { get; set; } = new()
        {
            Id = "exec-1",
            Status = SshExecutionStatusNames.Succeeded,
            Allowed = true,
            PolicyDecision = "allow",
            Profile = "runner",
            Command = "echo",
            Arguments = ["hello"],
            WorkingDirectory = "/tmp/project",
            ExitCode = 0,
            TimedOut = false,
            Stdout = "stdout chunk",
            Stderr = "stderr chunk",
            StdoutTruncated = false,
            StderrTruncated = false,
            ElapsedMilliseconds = 12,
            Summary = "SSH command completed.",
            TraceId = "trace-1",
            CreatedAt = FixedTimestamp,
            CompletedAt = FixedTimestamp.AddSeconds(1)
        };

        public ValueTask<Fin<SshExecutionResponse>> ExecuteAsync(SshExecutionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return new ValueTask<Fin<SshExecutionResponse>>(Fin.Succ(Response));
        }
    }

    private sealed class FakeSshAgentRuntime : ISshAgentRuntime
    {
        public SshAgentLaunchRequest? LastLaunchRequest { get; private set; }

        public string? LastStatusAgentId { get; private set; }

        public SshAgentOutputRequest? LastOutputRequest { get; private set; }

        public string? LastCancelAgentId { get; private set; }

        public SshAgentLaunchResponse LaunchResponse { get; set; } = new()
        {
            AgentId = "agent-1",
            Status = SshAgentStatusNames.Queued,
            Profile = "runner",
            Objective = "verify connectivity",
            CommandCount = 2,
            CurrentStep = 0,
            PollIntervalMilliseconds = 500,
            CreatedAt = FixedTimestamp,
            LastUpdatedAt = FixedTimestamp,
            Summary = "SSH agent queued."
        };

        public SshAgentStatusResponse StatusResponse { get; set; } = new()
        {
            AgentId = "agent-1",
            Status = SshAgentStatusNames.Working,
            Profile = "runner",
            Objective = "verify connectivity",
            CommandCount = 2,
            CurrentStep = 1,
            CompletedSteps = 1,
            FailedSteps = 0,
            CancellationRequested = false,
            CurrentCommand = "whoami",
            Summary = "SSH agent is running.",
            StdoutTail = "stdout tail",
            StderrTail = "stderr tail",
            StdoutLength = 42,
            StderrLength = 12,
            Steps =
            [
                new SshAgentStepSnapshot
                {
                    Index = 0,
                    Status = SshAgentStatusNames.Completed,
                    Command = "whoami",
                    Arguments = [],
                    ExitCode = 0,
                    Summary = "first step completed",
                    StartedAt = FixedTimestamp,
                    CompletedAt = FixedTimestamp.AddSeconds(1)
                }
            ],
            CreatedAt = FixedTimestamp,
            LastUpdatedAt = FixedTimestamp.AddSeconds(1),
            CompletedAt = null
        };

        public SshAgentOutputResponse OutputResponse { get; set; } = new()
        {
            AgentId = "agent-1",
            Status = SshAgentStatusNames.Working,
            Stdout = "stdout chunk",
            Stderr = "stderr chunk",
            StdoutOffset = 5,
            StderrOffset = 3,
            NextStdoutOffset = 17,
            NextStderrOffset = 16,
            StdoutTruncated = false,
            StderrTruncated = false
        };

        public SshAgentCancelResponse CancelResponse { get; set; } = new()
        {
            AgentId = "agent-1",
            Status = SshAgentStatusNames.Cancelled,
            CancellationRequested = true,
            Summary = "SSH agent cancellation requested.",
            LastUpdatedAt = FixedTimestamp.AddSeconds(2)
        };

        public ValueTask<Fin<SshAgentLaunchResponse>> LaunchAsync(SshAgentLaunchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastLaunchRequest = request;
            return new ValueTask<Fin<SshAgentLaunchResponse>>(Fin.Succ(LaunchResponse));
        }

        public ValueTask<Fin<SshAgentStatusResponse>> GetStatusAsync(string agentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastStatusAgentId = agentId;
            return new ValueTask<Fin<SshAgentStatusResponse>>(Fin.Succ(StatusResponse));
        }

        public ValueTask<Fin<SshAgentOutputResponse>> GetOutputAsync(SshAgentOutputRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastOutputRequest = request;
            return new ValueTask<Fin<SshAgentOutputResponse>>(Fin.Succ(OutputResponse));
        }

        public ValueTask<Fin<SshAgentCancelResponse>> CancelAsync(string agentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCancelAgentId = agentId;
            return new ValueTask<Fin<SshAgentCancelResponse>>(Fin.Succ(CancelResponse));
        }
    }

    private sealed class FakeAgentRunCoordinator : IAgentRunCoordinator
    {
        private readonly Dictionary<string, AgentRunSnapshot> _runs = new(StringComparer.Ordinal);
        private int _nextRunNumber = 1;

        public AgentRouterStartRunRequest? LastStartRequest { get; private set; }

        public AgentRouterApproveRunRequest? LastApproveRequest { get; private set; }

        public AgentRunId? LastCancelRunId { get; private set; }

        public ValueTask<Fin<AgentRouterStartRunResult>> StartAsync(
            in AgentRouterStartRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastStartRequest = request;

            var runId = new AgentRunId($"agent-run-{_nextRunNumber++}");
            var snapshot = new AgentRunSnapshot(
                runId,
                request.Objective,
                "queued",
                FixedTimestamp,
                FixedTimestamp,
                "AgentRouter run queued.",
                0,
                request.MetadataOrEmpty,
                null);

            _runs[runId.Value] = snapshot;

            return new ValueTask<Fin<AgentRouterStartRunResult>>(Fin.Succ(new AgentRouterStartRunResult(
                runId,
                snapshot.Status,
                snapshot.Message)));
        }

        public ValueTask<Fin<AgentRunSnapshot>> GetSnapshotAsync(
            AgentRunId runId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return _runs.TryGetValue(runId.Value, out var snapshot)
                ? new ValueTask<Fin<AgentRunSnapshot>>(Fin.Succ(snapshot))
                : new ValueTask<Fin<AgentRunSnapshot>>(Fin.Fail<AgentRunSnapshot>(Error.New($"agent run '{runId.Value}' was not found.")));
        }

        public ValueTask<Fin<AgentRunSnapshot>> ApproveAsync(
            in AgentRouterApproveRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastApproveRequest = request;

            if (!_runs.TryGetValue(request.RunId.Value, out var current))
            {
                return new ValueTask<Fin<AgentRunSnapshot>>(Fin.Fail<AgentRunSnapshot>(Error.New($"agent run '{request.RunId.Value}' was not found.")));
            }

            var metadata = new Dictionary<string, string?>(current.MetadataOrEmpty, StringComparer.OrdinalIgnoreCase)
            {
                [AgentRouterMetadataKeys.ApprovalGranted] = "true",
                [AgentRouterMetadataKeys.ApprovalId] = request.ApprovalId.Trim()
            };

            if (!string.IsNullOrWhiteSpace(request.ApprovedBy))
            {
                metadata[AgentRouterMetadataKeys.ApprovalApprovedBy] = request.ApprovedBy.Trim();
            }

            var approved = new AgentRunSnapshot(
                current.RunId,
                current.Objective,
                "queued",
                current.CreatedAtUtc,
                FixedTimestamp.AddMinutes(1),
                "AgentRouter run approved and re-queued.",
                current.Version + 1,
                metadata,
                null);

            _runs[request.RunId.Value] = approved;
            return new ValueTask<Fin<AgentRunSnapshot>>(Fin.Succ(approved));
        }

        public ValueTask<Fin<Unit>> CancelAsync(
            AgentRunId runId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCancelRunId = runId;

            if (!_runs.TryGetValue(runId.Value, out var current))
            {
                return new ValueTask<Fin<Unit>>(Fin.Fail<Unit>(Error.New($"agent run '{runId.Value}' was not found.")));
            }

            var cancelled = new AgentRunSnapshot(
                current.RunId,
                current.Objective,
                "cancelled",
                current.CreatedAtUtc,
                FixedTimestamp.AddMinutes(2),
                "AgentRouter run cancelled.",
                current.Version + 1,
                current.MetadataOrEmpty,
                null);

            _runs[runId.Value] = cancelled;
            return new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)));
        }
    }

    private static readonly DateTimeOffset FixedTimestamp = new(2025, 5, 28, 13, 45, 0, TimeSpan.Zero);
}
