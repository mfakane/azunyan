using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

namespace Azunote;

internal sealed record SingleInstanceCommand(
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string? StandardInput);

/// <summary>
/// Owns the process-wide Azunote instance and forwards command-line requests
/// from later launches over a named pipe.
/// </summary>
internal sealed class SingleInstanceHost : IDisposable
{
    private const int MaxArgumentCount = 64;
    private const int MaxStringBytes = 16 * 1024 * 1024;
    private const int ConnectionAttempts = 100;
    private const int ConnectionTimeoutMilliseconds = 250;
    private const int RetryDelayMilliseconds = 100;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private CancellationTokenSource? _serverCancellation;
    private Task? _serverTask;
    private Func<SingleInstanceCommand, Task>? _commandHandler;
    private bool _ownsMutex;
    private bool _disposed;

    internal SingleInstanceHost()
        : this("Azunote")
    {
    }

    internal SingleInstanceHost(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        _mutexName = $@"Local\{instanceName}.SingleInstance";
        _pipeName = $"{instanceName}.CommandLine";
    }

    internal bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(SingleInstanceHost));
        if (_ownsMutex)
        {
            return true;
        }

        var mutex = new Mutex(initiallyOwned: false, _mutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return false;
            }

            _mutex = mutex;
            _ownsMutex = true;
            return true;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    internal void Start(Func<SingleInstanceCommand, Task> commandHandler)
    {
        ArgumentNullException.ThrowIfNull(commandHandler);
        ObjectDisposedException.ThrowIf(_disposed, nameof(SingleInstanceHost));
        if (!_ownsMutex)
        {
            throw new InvalidOperationException("The single-instance mutex is not owned.");
        }

        if (_serverTask is not null)
        {
            throw new InvalidOperationException("The command-line server has already started.");
        }

        _commandHandler = commandHandler;
        _serverCancellation = new CancellationTokenSource();
        _serverTask = RunServerAsync(_serverCancellation.Token);
    }

    internal async Task ForwardAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ObjectDisposedException.ThrowIf(_disposed, nameof(SingleInstanceHost));

        if (_ownsMutex)
        {
            throw new InvalidOperationException("The primary instance cannot forward a command.");
        }

        using var client = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await ConnectAsync(client, cancellationToken).ConfigureAwait(false);
        await WriteCommandAsync(
            client,
            new SingleInstanceCommand(
                arguments,
                Environment.CurrentDirectory,
                standardInput),
            cancellationToken).ConfigureAwait(false);

        var response = new byte[1];
        await client.ReadExactlyAsync(response, cancellationToken).ConfigureAwait(false);
        if (response[0] != 1)
        {
            throw new InvalidOperationException("The running Azunote instance rejected the command.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _serverCancellation?.Cancel();
        if (_serverTask is not null)
        {
            try
            {
                _serverTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }

        _serverCancellation?.Dispose();
        if (_ownsMutex && _mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was already released by the operating system.
            }

            _mutex.Dispose();
        }

        _mutex = null;
        _ownsMutex = false;
        GC.SuppressFinalize(this);
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleClientAsync(server);
                server = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                ErrorReporter.LogException("Single-instance command server failure", exception);
                if (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server)
    {
        using (server)
        {
            try
            {
                var command = await ReadCommandAsync(server).ConfigureAwait(false);
                var handler = _commandHandler
                    ?? throw new InvalidOperationException("The command server is not initialized.");
                await handler(command).ConfigureAwait(false);
                await server.WriteAsync(new byte[] { 1 }).ConfigureAwait(false);
                await server.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The secondary process may exit before the response is sent.
            }
            catch (Exception exception)
            {
                ErrorReporter.LogException("Forwarded command-line processing failure", exception);
                try
                {
                    await server.WriteAsync(new byte[] { 0 }).ConfigureAwait(false);
                    await server.FlushAsync().ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static async Task ConnectAsync(
        NamedPipeClientStream client,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < ConnectionAttempts; attempt++)
        {
            try
            {
                await client.ConnectAsync(
                    ConnectionTimeoutMilliseconds,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException exception)
            {
                lastException = exception;
            }
            catch (IOException exception)
            {
                lastException = exception;
            }

            await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        throw new IOException("Could not connect to the running Azunote instance.", lastException);
    }

    private static async Task WriteCommandAsync(
        Stream stream,
        SingleInstanceCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Arguments.Count > MaxArgumentCount)
        {
            throw new InvalidOperationException("Too many command-line arguments.");
        }

        await WriteInt32Async(stream, command.Arguments.Count, cancellationToken).ConfigureAwait(false);
        foreach (var argument in command.Arguments)
        {
            await WriteStringAsync(stream, argument, cancellationToken).ConfigureAwait(false);
        }

        await WriteStringAsync(stream, command.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        if (command.StandardInput is null)
        {
            await stream.WriteAsync(new byte[] { 0 }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await stream.WriteAsync(new byte[] { 1 }, cancellationToken).ConfigureAwait(false);
            await WriteStringAsync(stream, command.StandardInput, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SingleInstanceCommand> ReadCommandAsync(Stream stream)
    {
        var count = await ReadInt32Async(stream).ConfigureAwait(false);
        if (count < 0 || count > MaxArgumentCount)
        {
            throw new InvalidDataException("The forwarded command contains an invalid argument count.");
        }

        var arguments = new string[count];
        for (var index = 0; index < count; index++)
        {
            arguments[index] = await ReadStringAsync(stream).ConfigureAwait(false);
        }

        var workingDirectory = await ReadStringAsync(stream).ConfigureAwait(false);
        var hasStandardInput = new byte[1];
        await stream.ReadExactlyAsync(hasStandardInput).ConfigureAwait(false);
        return hasStandardInput[0] switch
        {
            0 => new SingleInstanceCommand(arguments, workingDirectory, null),
            1 => new SingleInstanceCommand(
                arguments,
                workingDirectory,
                await ReadStringAsync(stream).ConfigureAwait(false)),
            _ => throw new InvalidDataException("The forwarded command contains an invalid standard-input flag.")
        };
    }

    private static async Task WriteInt32Async(
        Stream stream,
        int value,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadInt32Async(Stream stream)
    {
        var buffer = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(buffer).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static async Task WriteStringAsync(
        Stream stream,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Utf8.GetBytes(value);
        if (bytes.Length > MaxStringBytes)
        {
            throw new InvalidOperationException("A forwarded command argument is too large.");
        }

        await WriteInt32Async(stream, bytes.Length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadStringAsync(Stream stream)
    {
        var length = await ReadInt32Async(stream).ConfigureAwait(false);
        if (length < 0 || length > MaxStringBytes)
        {
            throw new InvalidDataException("The forwarded command contains an invalid string length.");
        }

        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes).ConfigureAwait(false);
        return Utf8.GetString(bytes);
    }
}
