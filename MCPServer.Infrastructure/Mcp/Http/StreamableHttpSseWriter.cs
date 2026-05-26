using System.Text;

namespace MCPServer.Infrastructure.Mcp.Http;

internal static class StreamableHttpSseWriter
{
    private static readonly ReadOnlyMemory<byte> NewLine = new byte[] { (byte)'\n' };

    public static async ValueTask WriteAsync(Stream output, StreamableHttpSseEvent eventData, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(eventData);

        using var buffer = new MemoryStream();

        if (!string.IsNullOrWhiteSpace(eventData.Id))
        {
            await WriteLineAsync(buffer, "id: " + eventData.Id, cancellationToken).ConfigureAwait(false);
        }

        if (eventData.RetryMilliseconds is { } retry)
        {
            await WriteLineAsync(buffer, "retry: " + retry.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        }

        if (eventData.Data.Length == 0)
        {
            await WriteLineAsync(buffer, "data:", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var lines = eventData.Data.Split('\n');
            foreach (var line in lines)
            {
                await WriteLineAsync(buffer, "data: " + line.TrimEnd('\r'), cancellationToken).ConfigureAwait(false);
            }
        }

        await WriteLineAsync(buffer, string.Empty, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        await buffer.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask WriteCommentAsync(Stream output, string comment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);

        using var buffer = new MemoryStream();
        await WriteLineAsync(buffer, ": " + comment, cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(buffer, string.Empty, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        await buffer.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteLineAsync(Stream output, string line, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
    }
}
