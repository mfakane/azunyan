using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azunyan.Core;

namespace Azunote;

/// <summary>
/// A language server resolved for one workspace folder, ready to start. An
/// empty <see cref="WorkspaceFolder"/> stands for the files outside every
/// workspace folder.
/// </summary>
internal sealed record LanguageServerLaunch(
    LanguageServerDefinition Definition,
    ExternalToolLaunchPlan Plan,
    IReadOnlyList<string> Arguments,
    string WorkspaceFolder)
{
    public string Key => Definition.ProcessKey + "\0" + WorkspaceFolder.ToUpperInvariant();
}

/// <summary>Problems a server published for a document, with the text they are about.</summary>
internal sealed record LanguageServerDiagnostics(string Uri, string Text, JsonArray Diagnostics);

/// <summary>
/// One running language server: the protocol on top of its standard input and
/// output, and the documents it has open. A document opened from several
/// windows is opened once and closed when the last of them closes it. The
/// whole text is sent on every change, which every server accepts.
/// </summary>
internal sealed class LanguageServerClient : IAsyncDisposable
{
    private const int ErrorOutputLines = 20;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly JsonRpcConnection _connection;
    private readonly Process? _process;
    private readonly JsonObject? _settings;
    private readonly string _workspaceFolder;
    private readonly SemaphoreSlim _documentsLock = new(1, 1);
    private readonly Dictionary<string, OpenDocument> _documents = new(StringComparer.Ordinal);
    private readonly Queue<string> _errorOutput = new();
    private readonly LanguageServerLog? _log;
    private bool _sendsOpenClose = true;
    private bool _sendsChanges = true;
    private bool _sendsSave;
    private bool _sendsSaveText;
    private int _disposed;

    internal LanguageServerClient(
        Stream fromServer,
        Stream toServer,
        string workspaceFolder,
        JsonObject? settings,
        Process? process = null,
        LanguageServerLog? log = null)
    {
        _workspaceFolder = workspaceFolder;
        _settings = settings;
        _process = process;
        _log = log;
        _connection = new JsonRpcConnection(fromServer, toServer, HandleRequestAsync, HandleNotification, log);
        _connection.Start();
    }

    /// <summary>Raised on the reading task when the server publishes problems for an open document.</summary>
    public event Action<LanguageServerDiagnostics>? DiagnosticsPublished;

    /// <summary>Raised on the reading task when the server asks for its inlay hints again.</summary>
    public event Action? InlayHintsChanged;

    /// <summary>Completes when the server stops talking, whether it exited or was stopped.</summary>
    public Task Completion => _connection.Completion;

    public IReadOnlyList<string> CompletionTriggerCharacters { get; private set; } = [];

    public bool SupportsCompletion { get; private set; }

    public bool SupportsHover { get; private set; }

    public bool SupportsFormatting { get; private set; }

    public bool SupportsInlayHints { get; private set; }

    /// <summary>The last lines the server wrote to standard error, to explain a failure.</summary>
    public string ErrorOutput
    {
        get
        {
            lock (_errorOutput)
            {
                return string.Join(Environment.NewLine, _errorOutput);
            }
        }
    }

    public static LanguageServerClient Start(LanguageServerLaunch launch, LanguageServerLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.Plan.LauncherPath,
            WorkingDirectory = launch.WorkspaceFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        launch.Plan.AddArguments(startInfo, launch.Arguments);
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {launch.Definition.Command}.");
        var client = new LanguageServerClient(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream,
            launch.WorkspaceFolder,
            launch.Definition.Settings,
            process,
            log);
        _ = client.ReadErrorOutputAsync(process.StandardError);
        return client;
    }

    public async Task InitializeAsync(JsonObject? initializationOptions, CancellationToken cancellationToken)
    {
        var parameters = new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["clientInfo"] = new JsonObject { ["name"] = "Azunote" },
            ["rootPath"] = _workspaceFolder.Length == 0 ? null : _workspaceFolder,
            ["rootUri"] = _workspaceFolder.Length == 0 ? null : ToUri(_workspaceFolder),
            ["workspaceFolders"] = GetWorkspaceFolders(),
            ["capabilities"] = new JsonObject
            {
                ["general"] = new JsonObject { ["positionEncodings"] = new JsonArray("utf-16") },
                ["workspace"] = new JsonObject
                {
                    ["configuration"] = true,
                    ["workspaceFolders"] = true,
                    ["inlayHint"] = new JsonObject { ["refreshSupport"] = true }
                },
                ["textDocument"] = new JsonObject
                {
                    ["synchronization"] = new JsonObject { ["didSave"] = true },
                    ["completion"] = new JsonObject
                    {
                        ["completionItem"] = new JsonObject
                        {
                            // Some servers offer no completion without snippets.
                            // Snippets are inserted as plain text.
                            ["snippetSupport"] = true,
                            ["documentationFormat"] = new JsonArray("plaintext", "markdown")
                        }
                    },
                    ["hover"] = new JsonObject
                    {
                        ["contentFormat"] = new JsonArray("plaintext", "markdown")
                    },
                    ["inlayHint"] = new JsonObject(),
                    ["publishDiagnostics"] = new JsonObject { ["versionSupport"] = true }
                }
            }
        };
        if (initializationOptions is not null)
        {
            parameters["initializationOptions"] = initializationOptions.DeepClone();
        }

        var result = await _connection.SendRequestAsync("initialize", parameters, cancellationToken)
            .ConfigureAwait(false);
        ReadCapabilities(result?["capabilities"] as JsonObject);
        await _connection.SendNotificationAsync("initialized", new JsonObject(), cancellationToken)
            .ConfigureAwait(false);
        if (_settings is not null)
        {
            await _connection.SendNotificationAsync(
                "workspace/didChangeConfiguration",
                new JsonObject { ["settings"] = _settings.DeepClone() },
                cancellationToken).ConfigureAwait(false);
        }
    }

    public Task OpenAsync(string uri, string languageId, string text) =>
        WithDocumentsAsync(async () =>
        {
            if (_documents.TryGetValue(uri, out var document))
            {
                document.References++;
                await SendChangeAsync(uri, document, text).ConfigureAwait(false);
                return;
            }

            _documents[uri] = new OpenDocument(text);
            if (_sendsOpenClose)
            {
                await _connection.SendNotificationAsync("textDocument/didOpen", new JsonObject
                {
                    ["textDocument"] = new JsonObject
                    {
                        ["uri"] = uri,
                        ["languageId"] = languageId,
                        ["version"] = 1,
                        ["text"] = text
                    }
                }).ConfigureAwait(false);
            }
        });

    /// <summary>
    /// Opens a document again, so the server reports its problems anew even
    /// when the text is unchanged. A document other windows also have open is
    /// only brought up to date.
    /// </summary>
    public Task ReopenAsync(string uri, string languageId, string text) =>
        WithDocumentsAsync(async () =>
        {
            if (!_documents.TryGetValue(uri, out var document))
            {
                return;
            }

            if (document.References > 1 || !_sendsOpenClose)
            {
                await SendChangeAsync(uri, document, text).ConfigureAwait(false);
                return;
            }

            _documents[uri] = new OpenDocument(text);
            await _connection.SendNotificationAsync("textDocument/didClose", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri }
            }).ConfigureAwait(false);
            await _connection.SendNotificationAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = uri,
                    ["languageId"] = languageId,
                    ["version"] = 1,
                    ["text"] = text
                }
            }).ConfigureAwait(false);
        });

    public Task ChangeAsync(string uri, string text) =>
        WithDocumentsAsync(() => _documents.TryGetValue(uri, out var document)
            ? SendChangeAsync(uri, document, text)
            : Task.CompletedTask);

    public Task SaveAsync(string uri, string text) =>
        WithDocumentsAsync(async () =>
        {
            if (!_sendsSave || !_documents.TryGetValue(uri, out var document))
            {
                return;
            }

            await SendChangeAsync(uri, document, text).ConfigureAwait(false);
            var parameters = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } };
            if (_sendsSaveText)
            {
                parameters["text"] = text;
            }

            await _connection.SendNotificationAsync("textDocument/didSave", parameters).ConfigureAwait(false);
        });

    public Task CloseAsync(string uri) =>
        WithDocumentsAsync(async () =>
        {
            if (!_documents.TryGetValue(uri, out var document) || --document.References > 0)
            {
                return;
            }

            _documents.Remove(uri);
            if (_sendsOpenClose)
            {
                await _connection.SendNotificationAsync("textDocument/didClose", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = uri }
                }).ConfigureAwait(false);
            }
        });

    public Task<JsonNode?> CompletionAsync(string uri, LineColumn position, CancellationToken cancellationToken) =>
        _connection.SendRequestAsync("textDocument/completion", CreatePositionParameters(uri, position), cancellationToken);

    public Task<JsonNode?> HoverAsync(string uri, LineColumn position, CancellationToken cancellationToken) =>
        _connection.SendRequestAsync("textDocument/hover", CreatePositionParameters(uri, position), cancellationToken);

    public Task<JsonNode?> InlayHintAsync(string uri, LineColumn start, LineColumn end, CancellationToken cancellationToken) =>
        _connection.SendRequestAsync("textDocument/inlayHint", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = start.Line, ["character"] = start.Column },
                ["end"] = new JsonObject { ["line"] = end.Line, ["character"] = end.Column }
            }
        }, cancellationToken);

    public Task<JsonNode?> FormattingAsync(string uri, IndentationSettings indentation, CancellationToken cancellationToken) =>
        _connection.SendRequestAsync("textDocument/formatting", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["options"] = new JsonObject
            {
                ["tabSize"] = indentation.Size,
                ["insertSpaces"] = indentation.Kind == IndentationKind.Spaces
            }
        }, cancellationToken);

    /// <summary>Asks the server to exit, and ends the process if it does not.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (!_connection.Completion.IsCompleted)
        {
            try
            {
                using var timeout = new CancellationTokenSource(ShutdownTimeout);
                await _connection.SendRequestAsync("shutdown", null, timeout.Token).ConfigureAwait(false);
                await _connection.SendNotificationAsync("exit", null, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                or ObjectDisposedException
                or OperationCanceledException
                or JsonRpcException)
            {
                // The server is stopped below whether or not it agreed.
            }
        }

        if (_process is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(ShutdownTimeout);
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(_process);
            }
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        _process?.Dispose();
        _documentsLock.Dispose();
    }

    internal static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    /// <summary>
    /// Reads a file URI the way a server may have written it, such as with a
    /// lowercase drive letter or an escaped colon.
    /// </summary>
    internal static string? ToPath(string uri)
    {
        try
        {
            var parsed = new Uri(Uri.UnescapeDataString(uri));
            return parsed.IsFile ? Path.GetFullPath(parsed.LocalPath) : null;
        }
        catch (Exception exception) when (exception is UriFormatException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static JsonObject CreatePositionParameters(string uri, LineColumn position) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = uri },
        ["position"] = new JsonObject { ["line"] = position.Line, ["character"] = position.Column }
    };

    private async Task SendChangeAsync(string uri, OpenDocument document, string text)
    {
        if (string.Equals(document.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        document.Text = text;
        document.Version++;
        if (!_sendsChanges)
        {
            return;
        }

        await _connection.SendNotificationAsync("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = document.Version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text })
        }).ConfigureAwait(false);
    }

    private async Task WithDocumentsAsync(Func<Task> action)
    {
        await _documentsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            _documentsLock.Release();
        }
    }

    private void ReadCapabilities(JsonObject? capabilities)
    {
        switch (capabilities?["textDocumentSync"])
        {
            case JsonValue kind when kind.GetValueKind() == JsonValueKind.Number:
                _sendsChanges = kind.GetValue<int>() != 0;
                break;
            case JsonObject sync:
                _sendsOpenClose = sync["openClose"]?.GetValueKind() == JsonValueKind.True;
                _sendsChanges = sync["change"]?.GetValueKind() == JsonValueKind.Number
                    && sync["change"]!.GetValue<int>() != 0;
                switch (sync["save"])
                {
                    case JsonObject save:
                        _sendsSave = true;
                        _sendsSaveText = save["includeText"]?.GetValueKind() == JsonValueKind.True;
                        break;
                    case JsonValue save when save.GetValueKind() == JsonValueKind.True:
                        _sendsSave = true;
                        break;
                }

                break;
        }

        if (capabilities?["completionProvider"] is JsonObject completion)
        {
            SupportsCompletion = true;
            CompletionTriggerCharacters = completion["triggerCharacters"] is JsonArray triggers
                ? [.. triggers.Select(trigger => trigger?.GetValue<string>()).OfType<string>().Where(trigger => trigger.Length > 0)]
                : [];
        }

        SupportsHover = IsProvided(capabilities?["hoverProvider"]);
        SupportsFormatting = IsProvided(capabilities?["documentFormattingProvider"]);
        SupportsInlayHints = IsProvided(capabilities?["inlayHintProvider"]);
    }

    private static bool IsProvided(JsonNode? provider) => provider switch
    {
        JsonObject => true,
        JsonValue value => value.GetValueKind() == JsonValueKind.True,
        _ => false
    };

    private Task<JsonNode?> HandleRequestAsync(string method, JsonNode? parameters) => method switch
    {
        "workspace/configuration" => Task.FromResult<JsonNode?>(GetConfiguration(parameters?["items"] as JsonArray)),
        "workspace/workspaceFolders" => Task.FromResult<JsonNode?>(GetWorkspaceFolders()),
        "workspace/applyEdit" => Task.FromResult<JsonNode?>(new JsonObject { ["applied"] = false }),
        "workspace/inlayHint/refresh" => RefreshInlayHints(),
        "client/registerCapability"
            or "client/unregisterCapability"
            or "window/workDoneProgress/create"
            or "window/showMessageRequest" => Task.FromResult<JsonNode?>(null),
        _ => Task.FromException<JsonNode?>(new JsonRpcException(
            JsonRpcException.MethodNotFound,
            $"Azunote does not handle {method}."))
    };

    private Task<JsonNode?> RefreshInlayHints()
    {
        InlayHintsChanged?.Invoke();
        return Task.FromResult<JsonNode?>(null);
    }

    private JsonArray? GetWorkspaceFolders() =>
        _workspaceFolder.Length == 0
            ? null
            : new JsonArray(new JsonObject
            {
                ["uri"] = ToUri(_workspaceFolder),
                ["name"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(_workspaceFolder))
            });

    private JsonArray GetConfiguration(JsonArray? items)
    {
        var result = new JsonArray();
        foreach (var item in items ?? [])
        {
            JsonNode? value = _settings;
            if (item?["section"]?.GetValue<string>() is { Length: > 0 } section)
            {
                foreach (var name in section.Split('.'))
                {
                    value = (value as JsonObject)?[name];
                }
            }

            result.Add(value?.DeepClone());
        }

        return result;
    }

    private void HandleNotification(string method, JsonNode? parameters)
    {
        if (method != "textDocument/publishDiagnostics"
            || parameters?["uri"]?.GetValue<string>() is not { } uri
            || parameters["diagnostics"] is not JsonArray diagnostics)
        {
            return;
        }

        string? text = null;
        _documentsLock.Wait();
        try
        {
            var document = _documents
                .FirstOrDefault(entry => string.Equals(ToPath(entry.Key), ToPath(uri), StringComparison.OrdinalIgnoreCase));
            var version = parameters["version"]?.GetValueKind() == JsonValueKind.Number
                ? parameters["version"]!.GetValue<int>()
                : (int?)null;
            if (document.Value is { } open && (version is null || version == open.Version))
            {
                uri = document.Key;
                text = open.Text;
            }
        }
        finally
        {
            _documentsLock.Release();
        }

        if (text is not null)
        {
            DiagnosticsPublished?.Invoke(new LanguageServerDiagnostics(uri, text, diagnostics));
        }
    }

    private async Task ReadErrorOutputAsync(StreamReader errorOutput)
    {
        try
        {
            while (await errorOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                _log?.Write($"stderr: {line}");
                lock (_errorOutput)
                {
                    _errorOutput.Enqueue(line);
                    if (_errorOutput.Count > ErrorOutputLines)
                    {
                        _errorOutput.Dequeue();
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process ended.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It exited in the meantime.
        }
    }

    private sealed class OpenDocument(string text)
    {
        public string Text { get; set; } = text;

        public int Version { get; set; } = 1;

        public int References { get; set; } = 1;
    }
}
