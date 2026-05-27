using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LanguageExt;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace MCPServer.Ssh.Infrastructure;

public sealed class SshNetCommandExecutor : ISshCommandExecutor
{
    private readonly ISshPathResolver _pathResolver;
    private readonly ISshCredentialResolver _credentialResolver;
    private readonly ILogger<SshNetCommandExecutor> _logger;

    public SshNetCommandExecutor(
        ISshPathResolver pathResolver,
        ISshCredentialResolver credentialResolver,
        ILogger<SshNetCommandExecutor> logger)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<SshCommandExecutionResult>> ExecuteAsync(SshExecutionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.ProfileName))
        {
            return Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New("SSH profile is required."));
        }

        if (string.IsNullOrWhiteSpace(command.Host))
        {
            return Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New($"SSH profile '{command.ProfileName}' is missing host."));
        }

        if (string.IsNullOrWhiteSpace(command.Username))
        {
            return Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New($"SSH profile '{command.ProfileName}' is missing username."));
        }

        try
        {
            SshAuthenticationException? lastAuthenticationFailure = null;
            var attempts = await CreateConnectionAttemptsAsync(command, cancellationToken).ConfigureAwait(false);
            foreach (var attempt in attempts)
            {
                try
                {
                    return Fin.Succ<SshCommandExecutionResult>(await ExecuteSshCommandAsync(command, attempt.ConnectionInfo, cancellationToken).ConfigureAwait(false));
                }
                catch (SshAuthenticationException ex)
                {
                    lastAuthenticationFailure = ex;
                    _logger.LogWarning(ex, "SSH authentication attempt {Attempt} failed for profile {Profile}", attempt.Description, command.ProfileName);
                }
            }

            return lastAuthenticationFailure is null
                ? Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New($"SSH profile '{command.ProfileName}' did not provide a usable authentication method."))
                : Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New($"SSH authentication failed for profile '{command.ProfileName}': {lastAuthenticationFailure.Message}"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SshOperationTimeoutException ex)
        {
            return Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New($"SSH command timed out for profile '{command.ProfileName}' after {command.TimeoutSeconds}s: {ex.Message}"));
        }
        catch (SshConnectionException ex)
        {
            return Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New($"SSH connection failed for profile '{command.ProfileName}': {ex.Message}"));
        }
        catch (SocketException ex)
        {
            return Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New($"SSH connection failed for profile '{command.ProfileName}': {ex.Message}"));
        }
        catch (SshException ex) when (ex.Message.Contains("host key", StringComparison.OrdinalIgnoreCase))
        {
            return Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New($"SSH host key validation failed for profile '{command.ProfileName}': {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Fin.Fail<SshCommandExecutionResult>(LanguageExt.Common.Error.New($"Failed to execute SSH command via profile '{command.ProfileName}': {ex.Message}"));
        }
    }

    private async ValueTask<SshCommandExecutionResult> ExecuteSshCommandAsync(
        SshExecutionCommand command,
        ConnectionInfo connectionInfo,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var client = CreateClient(command, connectionInfo, out var hostKeyValidation);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(Math.Max(1, command.TimeoutSeconds));

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (hostKeyValidation.WasRejected && ex is SshException)
        {
            throw new SshException(hostKeyValidation.CreateFailureMessage(command), ex);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var sshCommand = client.CreateCommand(BuildRemoteCommand(command));
            sshCommand.CommandTimeout = TimeSpan.FromSeconds(Math.Max(1, command.TimeoutSeconds));

            try
            {
                await Task.Run(() => sshCommand.Execute(), cancellationToken).ConfigureAwait(false);
            }
            catch (SshOperationTimeoutException timeoutException)
            {
                stopwatch.Stop();
                var stdoutTruncated = false;
                var stderrTruncated = false;
                return new SshCommandExecutionResult
                {
                    ExitCode = -1,
                    TimedOut = true,
                    Stdout = Truncate(sshCommand.Result, command.MaxOutputChars, ref stdoutTruncated),
                    Stderr = Truncate(string.IsNullOrWhiteSpace(sshCommand.Error) ? timeoutException.Message : sshCommand.Error, command.MaxOutputChars, ref stderrTruncated),
                    StdoutTruncated = stdoutTruncated,
                    StderrTruncated = stderrTruncated,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }

            stopwatch.Stop();
            var outputTruncated = false;
            var errorTruncated = false;
            return new SshCommandExecutionResult
            {
                ExitCode = sshCommand.ExitStatus.GetValueOrDefault(-1),
                Stdout = Truncate(sshCommand.Result, command.MaxOutputChars, ref outputTruncated),
                Stderr = Truncate(sshCommand.Error, command.MaxOutputChars, ref errorTruncated),
                StdoutTruncated = outputTruncated,
                StderrTruncated = errorTruncated,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    private SshClient CreateClient(SshExecutionCommand command, ConnectionInfo connectionInfo, out SshHostKeyValidationState hostKeyValidation)
    {
        hostKeyValidation = new SshHostKeyValidationState();
        var captured = hostKeyValidation;
        var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, args) =>
        {
            var decision = EvaluateHostKey(command, args.HostKey);
            args.CanTrust = decision.Trusted;
            if (!decision.Trusted)
            {
                captured.MarkRejected(decision.ExpectedSha256, decision.ActualSha256);
            }
        };

        return client;
    }

    private async ValueTask<IReadOnlyList<SshConnectionAttempt>> CreateConnectionAttemptsAsync(
        SshExecutionCommand command,
        CancellationToken cancellationToken)
    {
        var attempts = new List<SshConnectionAttempt>();

        if (!string.IsNullOrWhiteSpace(command.PrivateKeyPath))
        {
            var privateKeyPath = _pathResolver.ResolveConfiguredPath(command.PrivateKeyPath);
            if (!File.Exists(privateKeyPath))
            {
                throw new InvalidOperationException($"Private key file was not found: {privateKeyPath}");
            }

            var passphrase = await ResolveOptionalCredentialAsync(command.PrivateKeyPassphraseCredentialReference, cancellationToken).ConfigureAwait(false);
            var privateKeyFile = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(privateKeyPath)
                : new PrivateKeyFile(privateKeyPath, passphrase);

            attempts.Add(new SshConnectionAttempt(
                new ConnectionInfo(command.Host, command.Port <= 0 ? 22 : command.Port, command.Username,
                    new AuthenticationMethod[]
                    {
                        new PrivateKeyAuthenticationMethod(command.Username, privateKeyFile)
                    }),
                "private-key"));
        }

        var password = await ResolveOptionalCredentialAsync(command.PasswordCredentialReference, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(password))
        {
            attempts.Add(new SshConnectionAttempt(
                new ConnectionInfo(command.Host, command.Port <= 0 ? 22 : command.Port, command.Username,
                    new AuthenticationMethod[]
                    {
                        CreateKeyboardInteractiveAuthenticationMethod(command.Username, password)
                    }),
                "keyboard-interactive"));
            attempts.Add(new SshConnectionAttempt(
                new ConnectionInfo(command.Host, command.Port <= 0 ? 22 : command.Port, command.Username,
                    new AuthenticationMethod[]
                    {
                        new PasswordAuthenticationMethod(command.Username, password)
                    }),
                "password"));
        }

        if (attempts.Count == 0)
        {
            throw new InvalidOperationException(CreateMissingCredentialMessage(command));
        }

        return attempts;
    }

    private static string CreateMissingCredentialMessage(SshExecutionCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.PrivateKeyPath))
        {
            return $"SSH profile '{command.ProfileName}' has a privateKeyPath configured, but no usable authentication attempt could be created.";
        }

        if (command.PasswordCredentialReference is { Length: > 0 } credentialReference)
        {
            return $"SSH profile '{command.ProfileName}' uses credential reference '{credentialReference}', but no matching SQLite credential vault item was found. Store the secret with the sidecar vault command or configure privateKeyPath. Raw credentials are not accepted in tool requests.";
        }

        return $"SSH profile '{command.ProfileName}' must configure privateKeyPath or a password credential reference. Raw credentials are not accepted in tool requests.";
    }

    private static KeyboardInteractiveAuthenticationMethod CreateKeyboardInteractiveAuthenticationMethod(string username, string password)
    {
        var method = new KeyboardInteractiveAuthenticationMethod(username);
        method.AuthenticationPrompt += (_, args) =>
        {
            foreach (var prompt in args.Prompts)
            {
                prompt.Response = password;
            }
        };

        return method;
    }

    private async ValueTask<string?> ResolveOptionalCredentialAsync(
        string? credentialReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentialReference))
        {
            return null;
        }

        return await _credentialResolver.ResolveSecretAsync(credentialReference, cancellationToken).ConfigureAwait(false);
    }

    private static SshHostKeyDecision EvaluateHostKey(SshExecutionCommand command, byte[] hostKey)
    {
        var actual = "SHA256:" + ComputeSha256Fingerprint(hostKey);

        if (!string.IsNullOrWhiteSpace(command.HostKeySha256))
        {
            var expected = "SHA256:" + NormalizeSha256Fingerprint(command.HostKeySha256);
            return new SshHostKeyDecision(
                string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                expected,
                actual);
        }

        return new SshHostKeyDecision(command.AcceptUnknownHostKey, command.AcceptUnknownHostKey ? "accept_unknown" : "not_configured", actual);
    }

    private static string ComputeSha256Fingerprint(byte[] hostKey)
    {
        var hash = SHA256.HashData(hostKey);
        return Convert.ToBase64String(hash).TrimEnd('=');
    }

    private static string NormalizeSha256Fingerprint(string fingerprint)
    {
        var normalized = fingerprint.Trim();
        if (normalized.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..];
        }

        return normalized.TrimEnd('=');
    }

    private static string BuildRemoteCommand(SshExecutionCommand command)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(command.OperationKey))
        {
            builder.Append("MCP_OPERATION_KEY=");
            AppendQuotedPosix(builder, command.OperationKey);
            builder.Append(' ');
        }

        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            builder.Append("cd ");
            AppendQuotedPosix(builder, command.WorkingDirectory);
            builder.Append(" && ");
        }

        builder.Append(QuoteForPosixShell(command.Command));
        foreach (var argument in command.Arguments)
        {
            builder.Append(' ');
            builder.Append(QuoteForPosixShell(argument));
        }

        return builder.ToString();
    }

    private static string QuoteForPosixShell(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "''";
        }

        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static void AppendQuotedPosix(StringBuilder builder, string value)
    {
        builder.Append('\'');
        builder.Append(value.Replace("'", "'\\''", StringComparison.Ordinal));
        builder.Append('\'');
    }

    private static string Truncate(string? value, int maxChars, ref bool truncated)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var limit = Math.Max(1, maxChars);
        if (value.Length <= limit)
        {
            return value;
        }

        truncated = true;
        return value[..limit];
    }

    private sealed record SshConnectionAttempt(ConnectionInfo ConnectionInfo, string Description);

    private sealed record SshHostKeyDecision(bool Trusted, string ExpectedSha256, string ActualSha256);

    private sealed class SshHostKeyValidationState
    {
        public bool WasRejected { get; private set; }

        public string ExpectedSha256 { get; private set; } = string.Empty;

        public string ActualSha256 { get; private set; } = string.Empty;

        public void MarkRejected(string expectedSha256, string actualSha256)
        {
            WasRejected = true;
            ExpectedSha256 = expectedSha256;
            ActualSha256 = actualSha256;
        }

        public string CreateFailureMessage(SshExecutionCommand command)
        {
            return $"SSH host key mismatch for profile '{command.ProfileName}' host '{command.Host}'. Expected {ExpectedSha256}; actual {ActualSha256}.";
        }
    }
}
