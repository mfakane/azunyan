using System.IO.Pipes;

namespace Azunote;

/// <summary>
/// Owns the process-wide Azunote instance and serves command-line requests
/// forwarded by later launches and by <c>azu.exe</c>. The wire format lives in
/// <see cref="SingleInstanceProtocol"/> so that the command-line client can
/// share it.
/// </summary>
internal sealed class SingleInstanceHost : IDisposable
{
    private const int ConnectionAttempts = 100;
    private const int ConnectionTimeoutMilliseconds = 250;
    private const int RetryDelayMilliseconds = 100;
    private const int ClientDrainTimeoutMilliseconds = 2000;

    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly object _clientsLock = new();
    private readonly List<Task> _clients = [];
    private Mutex? _mutex;
    private CancellationTokenSource? _serverCancellation;
    private Task? _serverTask;
    private Func<SingleInstanceCommand, Task<SingleInstanceResponse>>? _commandHandler;
    private bool _ownsMutex;
    private bool _disposed;

    internal SingleInstanceHost()
        : this(SingleInstanceProtocol.DefaultInstanceName)
    {
    }

    internal SingleInstanceHost(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        _mutexName = SingleInstanceProtocol.GetMutexName(instanceName);
        _pipeName = SingleInstanceProtocol.GetPipeName(instanceName);
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

    internal void Start(Func<SingleInstanceCommand, Task<SingleInstanceResponse>> commandHandler)
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

    /// <summary>
    /// Forwards a command line from a second launch of the editor itself.
    /// Only the status is returned: a Windows subsystem executable has no
    /// standard output a shell can read, so output belongs to azu.exe.
    /// </summary>
    internal async Task<SingleInstanceStatus> ForwardAsync(
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
        ForegroundHandoff.AllowFor(client);
        await SingleInstanceProtocol.WriteCommandAsync(
            client,
            new SingleInstanceCommand(
                arguments,
                Environment.CurrentDirectory,
                standardInput),
            cancellationToken).ConfigureAwait(false);

        var response = await SingleInstanceProtocol
            .ReadResponseAsync(client, cancellationToken)
            .ConfigureAwait(false);
        return response.Status;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // A client is blocked waiting for its response, and the editor is
        // often shutting down precisely because the window that client was
        // waiting for has closed. Let the answers that are already on their
        // way finish before the server goes down.
        WaitForClients();
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

    private void TrackClient(Task client)
    {
        lock (_clientsLock)
        {
            _clients.RemoveAll(tracked => tracked.IsCompleted);
            _clients.Add(client);
        }
    }

    /// <summary>
    /// Waits for the commands that are still being answered. The wait is
    /// bounded so that shutdown cannot be held up by a client that stopped
    /// reading.
    /// </summary>
    private void WaitForClients()
    {
        Task[] pending;
        lock (_clientsLock)
        {
            pending = _clients.Where(tracked => !tracked.IsCompleted).ToArray();
        }

        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            Task.WaitAll(pending, ClientDrainTimeoutMilliseconds);
        }
        catch (Exception exception) when (exception is AggregateException
            or OperationCanceledException)
        {
        }
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
                TrackClient(HandleClientAsync(server));
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
                var command = await SingleInstanceProtocol.ReadCommandAsync(server).ConfigureAwait(false);
                var handler = _commandHandler
                    ?? throw new InvalidOperationException("The command server is not initialized.");
                var response = await handler(command).ConfigureAwait(false);
                await SingleInstanceProtocol.WriteResponseAsync(server, response).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The client may exit before the response is sent.
            }
            catch (Exception exception)
            {
                ErrorReporter.LogException("Forwarded command-line processing failure", exception);
                try
                {
                    await SingleInstanceProtocol
                        .WriteResponseAsync(server, SingleInstanceResponse.Failed())
                        .ConfigureAwait(false);
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
}
