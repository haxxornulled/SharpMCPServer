using System.Runtime.CompilerServices;

namespace MCPServer.Client.StreamableHttp;

internal static class McpSseReader
{
    public static async IAsyncEnumerable<McpSseEvent> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        string? line;
        string? id = null;
        int? retry = null;
        var data = new List<string>();

        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (line.Length == 0)
            {
                yield return new McpSseEvent
                {
                    Id = id,
                    RetryMilliseconds = retry,
                    Data = string.Join("\n", data)
                };

                id = null;
                retry = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            var name = separator < 0 ? line : line[..separator];
            var value = separator < 0 ? string.Empty : line[(separator + 1)..].TrimStart();
            switch (name)
            {
                case "id":
                    id = value;
                    break;
                case "retry":
                    if (int.TryParse(value, out var retryValue))
                    {
                        retry = retryValue;
                    }

                    break;
                case "data":
                    data.Add(value);
                    break;
            }
        }

        if (id is not null || retry is not null || data.Count != 0)
        {
            yield return new McpSseEvent
            {
                Id = id,
                RetryMilliseconds = retry,
                Data = string.Join("\n", data)
            };
        }
    }
}
