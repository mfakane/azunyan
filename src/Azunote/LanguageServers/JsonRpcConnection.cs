using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Azunote;

/// <summary>An error a JSON-RPC peer answered a request with.</summary>
internal sealed class JsonRpcException(int code, string message) : Exception(message)
{
    public const int MethodNotFound = -32601;
    public const int RequestCancelled = -32800;

    public int Code { get; } = code;
}

/// <summary>
/// JSON-RPC 2.0 over a pair of streams with the <c>Content-Length</c> framing
/// of the Language Server Protocol. Requests from the peer are answered by
/// <c>handleRequest</c>; notifications go to <c>handleNotification</c>. Both
/// run on the reading task, one message at a time.
/// </summary>
internal sealed class JsonRpcConnection : IAsyncDisposable
{
    private static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Func<string, JsonNode?, Task<JsonNode?>> _handleRequest;
    private readonly Action<string, JsonNode?> _handleNotification;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _closing = new();
    private long _nextId;
    private volatile bool _closed;
    private Task _reading = Task.CompletedTask;

    public JsonRpcConnection(
        Stream input,
        Stream output,
        Func<string, JsonNode?, Task<JsonNode?>> handleRequest,
        Action<string, JsonNode?> handleNotification)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _handleRequest = handleRequest ?? throw new ArgumentNullException(nameof(handleRequest));
        _handleNotification = handleNotification ?? throw new ArgumentNullException(nameof(handleNotification));
    }

    /// <summary>Completes when the peer closes its output or sends something unreadable.</summary>
    public Task Completion => _reading;

    public void Start() => _reading = Task.Run(ReadAsync);

    public async Task<JsonNode?> SendRequestAsync(
        string method,
        JsonNode? parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Interlocked.Increment(ref _nextId);
        var response = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = response;
        if (_closed)
        {
            _pending.TryRemove(id, out _);
            throw new IOException("The language server connection is closed.");
        }

        try
        {
            await WriteAsync(CreateMessage(method, parameters, id), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        await using var registration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.TrySetCanceled(cancellationToken);
                _ = SendCancelAsync(id);
            }
        }).ConfigureAwait(false);
        return await response.Task.ConfigureAwait(false);
    }

    public Task SendNotificationAsync(
        string method,
        JsonNode? parameters,
        CancellationToken cancellationToken = default) =>
        WriteAsync(CreateMessage(method, parameters, id: null), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _closed = true;
        _closing.Cancel();
        FailPending(new IOException("The language server connection is closed."));
        try
        {
            await _reading.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }

        _writeLock.Dispose();
        _closing.Dispose();
    }

    private static JsonObject CreateMessage(string method, JsonNode? parameters, long? id)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0" };
        if (id is { } value)
        {
            message["id"] = value;
        }

        message["method"] = method;
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        return message;
    }

    private async Task SendCancelAsync(long id)
    {
        try
        {
            await SendNotificationAsync("$/cancelRequest", new JsonObject { ["id"] = id }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The connection closed; there is nothing left to cancel.
        }
    }

    private async Task WriteAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var body = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(body))
        {
            message.WriteTo(writer);
        }

        var header = Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"Content-Length: {body.WrittenCount}\r\n\r\n"));
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(body.WrittenMemory, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadAsync()
    {
        var buffer = new List<byte>();
        var chunk = new byte[8192];
        var start = 0;
        Exception closed = new IOException("The language server closed its output.");
        try
        {
            while (true)
            {
                var headerEnd = IndexOf(buffer, HeaderEnd, start);
                if (headerEnd < 0)
                {
                    if (!await FillAsync(buffer, chunk).ConfigureAwait(false))
                    {
                        break;
                    }

                    continue;
                }

                var length = ParseContentLength(buffer, start, headerEnd);
                var bodyStart = headerEnd + HeaderEnd.Length;
                while (buffer.Count - bodyStart < length)
                {
                    if (!await FillAsync(buffer, chunk).ConfigureAwait(false))
                    {
                        throw new IOException("The language server closed its output inside a message.");
                    }
                }

                var body = buffer.GetRange(bodyStart, length).ToArray();
                buffer.RemoveRange(0, bodyStart + length);
                start = 0;
                await DispatchAsync(JsonNode.Parse(body)).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or FormatException or ObjectDisposedException)
        {
            closed = exception as IOException ?? new IOException(exception.Message, exception);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        finally
        {
            _closed = true;
            FailPending(closed);
        }
    }

    private async Task<bool> FillAsync(List<byte> buffer, byte[] chunk)
    {
        var read = await _input.ReadAsync(chunk, _closing.Token).ConfigureAwait(false);
        if (read == 0)
        {
            return false;
        }

        buffer.AddRange(chunk.AsSpan(0, read));
        return true;
    }

    private static int IndexOf(List<byte> buffer, byte[] value, int start)
    {
        for (var index = start; index <= buffer.Count - value.Length; index++)
        {
            var match = true;
            for (var offset = 0; offset < value.Length && match; offset++)
            {
                match = buffer[index + offset] == value[offset];
            }

            if (match)
            {
                return index;
            }
        }

        return -1;
    }

    private static int ParseContentLength(List<byte> buffer, int start, int end)
    {
        var header = Encoding.ASCII.GetString(buffer.GetRange(start, end - start).ToArray());
        foreach (var line in header.Split("\r\n"))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0
                && line[..separator].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                return int.Parse(line[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
            }
        }

        throw new FormatException("A message from the language server has no Content-Length.");
    }

    private async Task DispatchAsync(JsonNode? message)
    {
        if (message is not JsonObject body)
        {
            return;
        }

        var method = body["method"]?.GetValue<string>();
        var id = body["id"];
        if (method is null)
        {
            if (id?.GetValueKind() == JsonValueKind.Number
                && _pending.TryRemove(id.GetValue<long>(), out var pending))
            {
                if (body["error"] is JsonObject error)
                {
                    pending.TrySetException(new JsonRpcException(
                        error["code"]?.GetValue<int>() ?? 0,
                        error["message"]?.GetValue<string>() ?? "The language server reported an error."));
                }
                else
                {
                    pending.TrySetResult(body["result"]?.DeepClone());
                }
            }

            return;
        }

        var parameters = body["params"]?.DeepClone();
        if (id is null)
        {
            _handleNotification(method, parameters);
            return;
        }

        var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone() };
        try
        {
            response["result"] = await _handleRequest(method, parameters).ConfigureAwait(false);
        }
        catch (JsonRpcException exception)
        {
            response["error"] = new JsonObject { ["code"] = exception.Code, ["message"] = exception.Message };
        }

        await WriteAsync(response, _closing.Token).ConfigureAwait(false);
    }

    private void FailPending(Exception exception)
    {
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.TrySetException(exception);
            }
        }
    }
}
