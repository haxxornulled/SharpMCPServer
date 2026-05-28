namespace MCPServer.VisualStudio.Vsix;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

/// <summary>
/// Represents a small asynchronous command for WPF bindings.
/// </summary>
internal sealed class AsyncCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private int _isExecuting;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncCommand"/> class.
    /// </summary>
    /// <param name="executeAsync">The async delegate to execute.</param>
    /// <param name="canExecute">An optional predicate that determines whether the command can run.</param>
    public AsyncCommand(Func<CancellationToken, Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
        => Volatile.Read(ref _isExecuting) == 0 && (_canExecute?.Invoke() ?? true);

    /// <inheritdoc />
    public void Execute(object? parameter)
        => _ = ExecuteAsync(CancellationToken.None);

    /// <summary>
    /// Executes the command asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <returns>A task that completes when the command finishes.</returns>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isExecuting, 1) == 1)
        {
            return;
        }

        RaiseCanExecuteChanged();
        try
        {
            await _executeAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Raises the <see cref="CanExecuteChanged"/> event.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        var handler = CanExecuteChanged;
        if (handler is null)
        {
            return;
        }

        handler(this, EventArgs.Empty);
    }
}
