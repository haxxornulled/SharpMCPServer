namespace MCPServer.Inference.Infrastructure.Hosting;

public interface ILocalInferenceProviderHandle : IAsyncDisposable
{
    ValueTask StopAsync(CancellationToken cancellationToken);
}
