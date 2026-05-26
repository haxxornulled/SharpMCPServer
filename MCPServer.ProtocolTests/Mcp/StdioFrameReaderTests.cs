using System.Text;
using MCPServer.Infrastructure.Mcp.Stdio;
using Xunit;

namespace MCPServer.ProtocolTests.Mcp;

public sealed class StdioFrameReaderTests
{
    [Fact]
    public async Task ReadFrameAsync_Trims_Final_Carriage_Return_For_Crlf_Frames()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\"}\r\n"));
        await using var output = new MemoryStream();
        await using var session = StdioMcpTransportSession.Open(input, output, NewOptions());

        var read = await session.ReadFrameAsync(1_024, CancellationToken.None);

        Assert.False(read.IsEndOfInput);
        Assert.False(read.IsTooLarge);
        var frame = read.Frame;
        Assert.NotNull(frame);
        using (frame)
        {
            Assert.Equal("{\"jsonrpc\":\"2.0\"}", Encoding.UTF8.GetString(frame!.Memory.Span));
        }
    }



    [Fact]
    public async Task ReadFrameAsync_Trims_Crlf_When_CarriageReturn_And_LineFeed_Are_Read_Separately()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("{}\r\n"));
        await using var output = new MemoryStream();
        await using var session = StdioMcpTransportSession.Open(input, output, new StdioMcpTransportOptions
        {
            ReadBufferBytes = 3,
            InitialFrameBufferBytes = 2,
            MaxInputFrameBytes = 1_024,
            ClearReturnedInputBuffers = true
        });

        var read = await session.ReadFrameAsync(1_024, CancellationToken.None);

        Assert.False(read.IsInvalidFrame);
        Assert.False(read.IsTooLarge);
        Assert.False(read.IsEndOfInput);

        var frame = read.Frame;
        Assert.NotNull(frame);
        using (frame)
        {
            Assert.Equal("{}", Encoding.UTF8.GetString(frame!.Memory.Span));
        }
    }

    [Fact]
    public async Task ReadFrameAsync_Rejects_Embedded_Carriage_Returns()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\"}\r{\"jsonrpc\":\"2.0\"}\n{}\n"));
        await using var output = new MemoryStream();
        await using var session = StdioMcpTransportSession.Open(input, output, NewOptions());

        var invalid = await session.ReadFrameAsync(1_024, CancellationToken.None);
        Assert.True(invalid.IsInvalidFrame);
        Assert.False(invalid.IsTooLarge);
        Assert.False(invalid.IsEndOfInput);
        Assert.Null(invalid.Frame);

        var next = await session.ReadFrameAsync(1_024, CancellationToken.None);
        Assert.False(next.IsInvalidFrame);
        Assert.False(next.IsTooLarge);
        Assert.False(next.IsEndOfInput);

        var frame = next.Frame;
        Assert.NotNull(frame);
        using (frame)
        {
            Assert.Equal("{}", Encoding.UTF8.GetString(frame!.Memory.Span));
        }
    }

    [Fact]
    public async Task ReadFrameAsync_Rejects_Final_Frame_When_Input_Ends_Without_Newline_By_Default()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\"}"));
        await using var output = new MemoryStream();
        await using var session = StdioMcpTransportSession.Open(input, output, NewOptions());

        var read = await session.ReadFrameAsync(1_024, CancellationToken.None);

        Assert.True(read.IsInvalidFrame);
        Assert.False(read.IsEndOfInput);
        Assert.False(read.IsTooLarge);
        Assert.Null(read.Frame);
    }

    [Fact]
    public async Task ReadFrameAsync_Can_Optionally_Accept_Final_Frame_Without_Newline_For_Diagnostics()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\"}"));
        await using var output = new MemoryStream();
        var options = NewOptions(allowFinalFrameWithoutNewline: true);
        await using var session = StdioMcpTransportSession.Open(input, output, options);

        var read = await session.ReadFrameAsync(1_024, CancellationToken.None);

        Assert.False(read.IsInvalidFrame);
        Assert.False(read.IsEndOfInput);
        Assert.False(read.IsTooLarge);
        var frame = read.Frame;
        Assert.NotNull(frame);
        using (frame)
        {
            Assert.Equal("{\"jsonrpc\":\"2.0\"}", Encoding.UTF8.GetString(frame!.Memory.Span));
        }
    }

    [Fact]
    public async Task ReadFrameAsync_Reports_Oversized_Frame_And_Recovers_For_Next_Frame()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("abcdef\n{}\n"));
        await using var output = new MemoryStream();
        await using var session = StdioMcpTransportSession.Open(input, output, new StdioMcpTransportOptions
        {
            ReadBufferBytes = 3,
            InitialFrameBufferBytes = 2,
            MaxInputFrameBytes = 4,
            ClearReturnedInputBuffers = true
        });

        var tooLarge = await session.ReadFrameAsync(4, CancellationToken.None);
        Assert.True(tooLarge.IsTooLarge);
        Assert.False(tooLarge.IsEndOfInput);
        Assert.Null(tooLarge.Frame);

        var next = await session.ReadFrameAsync(4, CancellationToken.None);
        Assert.False(next.IsTooLarge);
        Assert.False(next.IsEndOfInput);
        var frame = next.Frame;
        Assert.NotNull(frame);
        using (frame)
        {
            Assert.Equal("{}", Encoding.UTF8.GetString(frame!.Memory.Span));
        }
    }

    [Fact]
    public async Task ReadFrameAsync_Returns_EndOfInput_After_All_Frames_Are_Read()
    {
        await using var input = new MemoryStream(Array.Empty<byte>());
        await using var output = new MemoryStream();
        await using var session = StdioMcpTransportSession.Open(input, output, NewOptions());

        var read = await session.ReadFrameAsync(1_024, CancellationToken.None);

        Assert.True(read.IsEndOfInput);
        Assert.False(read.IsTooLarge);
        Assert.Null(read.Frame);
    }

    private static StdioMcpTransportOptions NewOptions(bool allowFinalFrameWithoutNewline = false)
    {
        return new StdioMcpTransportOptions
        {
            ReadBufferBytes = 8,
            InitialFrameBufferBytes = 4,
            MaxInputFrameBytes = 1_024,
            ClearReturnedInputBuffers = true,
            AllowFinalFrameWithoutNewline = allowFinalFrameWithoutNewline
        };
    }
}
