using Microsoft.Extensions.Options;

namespace MCPServer.Ssh.Infrastructure;

internal sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    where TOptions : class
{
    private readonly TOptions _currentValue;

    public StaticOptionsMonitor(TOptions currentValue)
    {
        _currentValue = currentValue ?? throw new ArgumentNullException(nameof(currentValue));
    }

    public TOptions CurrentValue => _currentValue;

    public TOptions Get(string? name) => _currentValue;

    public IDisposable OnChange(Action<TOptions, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return NoOpDisposable.Instance;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
