using LanguageExt;
using Unit = LanguageExt.Unit;
using MCPServer.Tools.Ssh.Configuration;
using MCPServer.Tools.Ssh.Interfaces;
using MCPServer.Tools.Ssh.Models;
using Microsoft.Extensions.Options;

namespace MCPServer.Tools.Ssh.Infrastructure;

public sealed class FileSystemSshExecutionTraceWriter : ISshExecutionTraceWriter
{
    private readonly IOptionsMonitor<SshToolSettings> _settings;
    private readonly ISshPathResolver _pathResolver;

    public FileSystemSshExecutionTraceWriter(IOptionsMonitor<SshToolSettings> settings, ISshPathResolver pathResolver)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public async ValueTask<Fin<Unit>> WriteAsync(SshExecutionResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var directory = ResolveTraceDirectory();
            Directory.CreateDirectory(directory);

            var fileName = SanitizeFileName(response.TraceId) + ".json";
            var path = Path.Combine(directory, fileName);
            await File.WriteAllTextAsync(path, SshJson.ToTraceJson(response), cancellationToken).ConfigureAwait(false);

            return Fin.Succ<Unit>(Prelude.unit);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fin.Fail<Unit>(LanguageExt.Common.Error.New($"Failed to write SSH execution trace: {ex.Message}"));
        }
    }

    private string ResolveTraceDirectory()
    {
        var settings = SshToolSettings.Normalize(_settings.CurrentValue);
        if (!string.IsNullOrWhiteSpace(settings.TraceDirectory))
        {
            return _pathResolver.ResolveConfiguredPath(settings.TraceDirectory);
        }

        return _pathResolver.ResolveUserDataPath(Path.Combine("ssh", "traces"));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Where(character => !invalid.Contains(character))
            .ToArray();

        return chars.Length == 0 ? Guid.NewGuid().ToString("N") : new string(chars);
    }
}
