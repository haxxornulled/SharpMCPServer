using System.Text.Json;
using LanguageExt;
using MCPServer.Client.Tasking;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.UnitTests.Client;

public sealed class InMemoryMcpClientTaskRegistryTests
{
    [Fact]
    public async Task QueueTask_Completes_And_Exposes_Result()
    {
        var registry = new InMemoryMcpClientTaskRegistry();
        var notificationCount = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.TaskStatusChanged += (_, args) =>
        {
            notificationCount++;

            if (args.Status is McpTaskStatuses.Completed)
            {
                completed.TrySetResult();
            }
        };

        var created = registry.QueueTask(
            metadata: new McpTaskMetadata { Ttl = 60 },
            pollInterval: 10,
            statusMessage: "Queued",
            work: _ =>
            {
                using var document = JsonDocument.Parse("""{"ok":true}""");
                return new ValueTask<Fin<JsonElement>>(Fin.Succ(document.RootElement.Clone()));
            });

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var task = registry.GetTask(created.Task.TaskId);
        Assert.True(task.IsSucc);
        Assert.Equal(McpTaskStatuses.Completed, task.Match(Succ: value => value.Status, Fail: _ => string.Empty));

        var payload = registry.GetTaskResultPayload(created.Task.TaskId);
        Assert.True(payload.IsSucc);
        Assert.True(payload.Match(Succ: value => value.TryGetProperty("ok", out var ok) && ok.GetBoolean(), Fail: _ => false));
        Assert.True(notificationCount >= 2);
    }
}
