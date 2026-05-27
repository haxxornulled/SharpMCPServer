using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Net.Sockets;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure.Mcp.Http;
using Xunit;

namespace MCPServer.UnitTests.Client.Console;

public sealed class McpClientConsoleSmokeTests
{
    [Fact]
    public async Task Console_Can_Smoke_Test_A_Real_Streamable_Http_Host()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var port = GetAvailablePort();
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        using var hostProcess = StartProcess(
            "dotnet",
            [
                hostDll,
                "--McpTransport:Http:Enabled=true",
                $"--McpTransport:Http:Port={port}",
                "--McpTransport:Http:BindLoopbackOnly=true",
                "--McpTransport:Http:SseHeartbeatMilliseconds=100",
                "--McpTransport:Http:MaxSessionHistoryMessages=8"
            ],
            hostWorkingDirectory);

        try
        {
            await WaitForHttpTransportAsync(port, cancellationToken);

            var consoleResult = await RunProcessAsync(
                "dotnet",
                [
                    consoleDll,
                    "--endpoint",
                    $"http://127.0.0.1:{port}/mcp/",
                    "--open-server-event-stream",
                    "--tool",
                    "server.info",
                    "--arguments",
                    "{}"
                ],
                consoleWorkingDirectory,
                cancellationToken);

            Assert.Equal(0, consoleResult.ExitCode);
            Assert.Contains("Connected to", consoleResult.Stdout, StringComparison.Ordinal);
            Assert.Contains("Tools exposed by server:", consoleResult.Stdout, StringComparison.Ordinal);
            Assert.Contains("Calling tool: server.info", consoleResult.Stdout, StringComparison.Ordinal);
            Assert.Contains("Tool returned a success result.", consoleResult.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            await TryKillProcessAsync(hostProcess, cancellationToken);
        }
    }

    [Fact]
    public async Task Console_Can_Smoke_Test_A_Real_Stdio_Host()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        var consoleResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--working-directory",
                hostWorkingDirectory,
                "--tool",
                "server.info",
                "--arguments",
                "{}"
            ],
            consoleWorkingDirectory,
            cancellationToken);

        Assert.Equal(0, consoleResult.ExitCode);
        Assert.Contains("Connected to", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tools exposed by server:", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Calling tool: server.info", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tool returned a success result.", consoleResult.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_Can_Demo_Client_Sampling_Over_Stdio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        var consoleResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--working-directory",
                hostWorkingDirectory,
                "--demo-sampling",
                "--tool",
                "client.sample",
                "--arguments",
                "{\"prompt\":\"Say hello in one sentence.\"}"
            ],
            consoleWorkingDirectory,
            cancellationToken);

        Assert.Equal(0, consoleResult.ExitCode);
        Assert.Contains("Calling tool: client.sample", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Demo assistant reply:", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tool returned a success result.", consoleResult.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_Can_Select_Inference_Provider_With_Shortcut_Over_Stdio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var hostDll = GetProjectOutputPath("MCPServer.Host", "MCPServer.Host.dll", configuration);
        var consoleDll = GetProjectOutputPath("MCPServer.Client.Console", "MCPServer.Client.Console.dll", configuration);
        var hostWorkingDirectory = Path.GetDirectoryName(hostDll) ?? throw new InvalidOperationException("Host output directory was not found.");
        var consoleWorkingDirectory = Path.GetDirectoryName(consoleDll) ?? throw new InvalidOperationException("Console output directory was not found.");

        var consoleResult = await RunProcessAsync(
            "dotnet",
            [
                consoleDll,
                "--transport",
                "stdio",
                "--server-path",
                "dotnet",
                "--server-arg",
                hostDll,
                "--working-directory",
                hostWorkingDirectory,
                "--tool",
                "inference.generate",
                "--arguments",
                "{\"prompt\":\"Say hello in one sentence.\"}",
                "--provider",
                "lmstudio"
            ],
            consoleWorkingDirectory,
            cancellationToken);

        Assert.Equal(0, consoleResult.ExitCode);
        Assert.Contains("Calling tool: inference.generate", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("Tool returned a success result.", consoleResult.Stdout, StringComparison.Ordinal);
        Assert.Contains("\"providerId\":\"lmstudio\"", consoleResult.Stdout, StringComparison.Ordinal);
    }

    private static async Task WaitForHttpTransportAsync(int port, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var endpoint = new Uri($"http://127.0.0.1:{port}/mcp/", UriKind.Absolute);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var origin = endpoint.GetLeftPart(UriPartial.Authority);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await SendInitializeProbeAsync(httpClient, endpoint, origin, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    if (response.Headers.TryGetValues(StreamableHttpMcpHeaderNames.SessionId, out var sessionIds))
                    {
                        var sessionId = sessionIds.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(sessionId))
                        {
                            await SendDeleteProbeAsync(httpClient, endpoint, origin, sessionId, cancellationToken);
                        }
                    }

                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for the Streamable HTTP transport to start.");
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> SendInitializeProbeAsync(HttpClient httpClient, Uri endpoint, string origin, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Mcp-Method", McpMethods.Initialize);
        request.Content = new StringContent(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"smoke-test","version":"1"}}}
            """,
            Encoding.UTF8,
            "application/json");

        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task SendDeleteProbeAsync(HttpClient httpClient, Uri endpoint, string origin, string sessionId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", McpProtocolVersions.Current);
        request.Headers.TryAddWithoutValidation("MCP-Session-Id", sessionId);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"Expected HTTP 204 from DELETE probe, but received {(int)response.StatusCode}.");
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        using var process = StartProcess(fileName, arguments, workingDirectory);
        try
        {
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        finally
        {
            await TryKillProcessAsync(process, CancellationToken.None);
        }
    }

    private static Process StartProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
    }

    private static async Task TryKillProcessAsync(Process process, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GetProjectOutputPath(string projectName, string fileName, string configuration)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var path = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", projectName, "bin", configuration, "net10.0", fileName));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find built artifact '{fileName}' for project '{projectName}'.", path);
        }

        return path;
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
