using System.IO.Pipes;

namespace Azunote;

/// <summary>
/// Sends one command line to the running Azunote instance and reads its
/// response. This is the client half used by <c>azu.exe</c>; the editor owns
/// the server half in <c>SingleInstanceHost</c>.
/// </summary>
public static class SingleInstanceClient
{
    private const int ConnectionTimeoutMilliseconds = 250;
    private const int RetryDelayMilliseconds = 100;

    /// <summary>
    /// Sends a command line and waits for the running instance to finish
    /// processing it. Standard input is passed as UTF-8 so that it reaches the
    /// editor without being decoded on the way. Returns <see langword="null"/>
    /// when no instance answered within <paramref name="attempts"/> connection
    /// attempts.
    /// </summary>
    public static async Task<SingleInstanceResponse?> TrySendAsync(
        string instanceName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        ReadOnlyMemory<byte>? standardInput,
        int attempts = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempts);

        using var client = new NamedPipeClientStream(
            ".",
            SingleInstanceProtocol.GetPipeName(instanceName),
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        if (!await TryConnectAsync(client, attempts, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        ForegroundHandoff.AllowFor(client);

        await SingleInstanceProtocol
            .WriteCommandAsync(client, arguments, workingDirectory, standardInput, cancellationToken)
            .ConfigureAwait(false);
        return await SingleInstanceProtocol
            .ReadResponseAsync(client, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> TryConnectAsync(
        NamedPipeClientStream client,
        int attempts,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                await client
                    .ConnectAsync(ConnectionTimeoutMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }

            if (attempt + 1 < attempts)
            {
                await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }
}
