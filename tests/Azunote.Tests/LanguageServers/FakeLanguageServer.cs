using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using Azunote;

namespace Azunote.Tests.LanguageServers;

/// <summary>
/// A language server inside the test, talking to a client over a named
/// pipe. It records what the client sends and answers <c>initialize</c> with
/// <see cref="Capabilities"/>.
/// </summary>
internal sealed class FakeLanguageServer : IAsyncDisposable
{
    private readonly NamedPipeServerStream _server;
    private readonly NamedPipeClientStream _client;
    private readonly JsonRpcConnection _connection;

    public FakeLanguageServer(JsonObject? capabilities = null)
    {
        Capabilities = capabilities ?? new JsonObject
        {
            ["textDocumentSync"] = new JsonObject
            {
                ["openClose"] = true,
                ["change"] = 1,
                ["save"] = new JsonObject { ["includeText"] = true }
            },
            ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray(".") },
            ["hoverProvider"] = true
        };
        var name = $"azunote-test-{Guid.NewGuid():N}";
        _server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connected = _server.WaitForConnectionAsync();
        _client.Connect();
        connected.GetAwaiter().GetResult();
        _connection = new JsonRpcConnection(_server, _server, HandleRequestAsync, HandleNotification);
        _connection.Start();
    }

    public JsonObject Capabilities { get; }

    /// <summary>Answers for requests other than <c>initialize</c> and <c>shutdown</c>, by method.</summary>
    public ConcurrentDictionary<string, Func<JsonNode?, JsonNode?>> Handlers { get; } = new();

    public ConcurrentQueue<(string Method, JsonNode? Parameters)> Received { get; } = new();

    public LanguageServerClient CreateClient(string workspaceFolder, JsonObject? settings = null) =>
        new(_client, _client, workspaceFolder, settings);

    public Task<JsonNode?> RequestAsync(string method, JsonNode? parameters) =>
        _connection.SendRequestAsync(method, parameters);

    public Task NotifyAsync(string method, JsonNode? parameters) =>
        _connection.SendNotificationAsync(method, parameters);

    public IEnumerable<JsonNode?> ReceivedOf(string method) =>
        Received.Where(message => message.Method == method).Select(message => message.Parameters);

    /// <summary>Waits until the client has sent <paramref name="count"/> messages with this method.</summary>
    public async Task WaitForAsync(string method, int count = 1)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (ReceivedOf(method).Count() < count)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>Closes the pipe, as a server that exits does.</summary>
    public void Close() => _server.Dispose();

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        _server.Dispose();
        _client.Dispose();
    }

    private Task<JsonNode?> HandleRequestAsync(string method, JsonNode? parameters)
    {
        Received.Enqueue((method, parameters));
        return method switch
        {
            "initialize" => Task.FromResult<JsonNode?>(new JsonObject { ["capabilities"] = Capabilities.DeepClone() }),
            "shutdown" => Task.FromResult<JsonNode?>(null),
            _ when Handlers.TryGetValue(method, out var handler) => Task.FromResult(handler(parameters)),
            _ => Task.FromException<JsonNode?>(new JsonRpcException(JsonRpcException.MethodNotFound, method))
        };
    }

    private void HandleNotification(string method, JsonNode? parameters) =>
        Received.Enqueue((method, parameters));
}
