using System.Text.Json.Nodes;
using Azunyan.Core;

namespace Azunote;

internal enum LanguageServerIndicatorState
{
    Hidden,
    Off,
    Starting,
    Running,
    Failed
}

/// <summary>What the ⚡ in the status bar shows for the document.</summary>
internal sealed record LanguageServerIndicator(
    LanguageServerIndicatorState State,
    string? Command = null,
    string? Message = null,
    bool OutsideWorkspace = false)
{
    public static LanguageServerIndicator Hidden { get; } = new(LanguageServerIndicatorState.Hidden);
}

/// <summary>Completion and descriptions the editor gets from the document's language server.</summary>
internal interface ILanguageServerProviders
{
    /// <summary>The server's completion when it runs and offers some; otherwise <paramref name="fallback"/>.</summary>
    ICompletionProvider GetCompletionProvider(ICompletionProvider? fallback);

    ITooltipProvider TooltipProvider { get; }

    IReadOnlyList<string> CompletionTriggerCharacters { get; }
}

/// <summary>
/// Connects the document in one window to the language server of its mode
/// and workspace folder: decides what the ⚡ shows, holds the server while it
/// is turned on, keeps the server's copy of the document current, and puts the
/// problems it reports into the window's problems.
/// </summary>
internal sealed class LanguageServerController : ILanguageServerProviders, IDisposable
{
    internal const string DiagnosticsSourceKey = "languageServer";
    private static readonly TimeSpan ChangeDelay = TimeSpan.FromMilliseconds(200);

    private readonly IEditorBuffer _editor;
    private readonly Func<DocumentSessionState> _state;
    private readonly Func<string> _languageModeId;
    private readonly Func<LanguageServerDefinition?> _definition;
    private readonly Func<IReadOnlyList<string>> _workspacePatterns;
    private readonly LanguageServerRegistry _registry;
    private readonly DocumentDiagnostics _diagnostics;
    private readonly IUiDispatcher _dispatcher;
    private readonly ITimer _changeTimer;
    private Target? _target;
    private (string FilePath, string ModeId, LanguageServerDefinition Definition, IReadOnlyList<string> Patterns, Target? Target)? _resolved;
    private LanguageServerLease? _lease;
    private Active? _active;
    private bool _disposed;

    public LanguageServerController(
        IEditorBuffer editor,
        Func<DocumentSessionState> state,
        Func<string> languageModeId,
        Func<LanguageServerDefinition?> definition,
        Func<IReadOnlyList<string>> workspacePatterns,
        LanguageServerRegistry registry,
        DocumentDiagnostics diagnostics,
        IUiDispatcher dispatcher,
        TimeProvider? timeProvider = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _languageModeId = languageModeId ?? throw new ArgumentNullException(nameof(languageModeId));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _workspacePatterns = workspacePatterns ?? throw new ArgumentNullException(nameof(workspacePatterns));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _changeTimer = (timeProvider ?? TimeProvider.System).CreateTimer(
            _ => _dispatcher.TryEnqueue(SendChange),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _editor.Edited += Editor_Edited;
        _registry.EnablementChanged += Registry_EnablementChanged;
        TooltipProvider = new HoverProvider(this);
    }

    /// <summary>Raised on the UI thread when <see cref="Indicator"/> or the server's triggers change.</summary>
    public event EventHandler? Changed;

    public LanguageServerIndicator Indicator { get; private set; } = LanguageServerIndicator.Hidden;

    public ITooltipProvider TooltipProvider { get; }

    public IReadOnlyList<string> CompletionTriggerCharacters =>
        _active?.Client.CompletionTriggerCharacters ?? [];

    /// <summary>The log of the server the document holds, while it is turned on.</summary>
    public LanguageServerLog? Log => _lease?.Instance.Log;

    public ICompletionProvider GetCompletionProvider(ICompletionProvider? fallback) =>
        new CompletionProvider(this, fallback);

    /// <summary>
    /// Looks again at the document's file, mode and workspace folder, and
    /// whether its server is turned on there, after any of them changed.
    /// </summary>
    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        var target = ResolveTarget();
        if (target is null)
        {
            Detach();
            _target = null;
            SetIndicator(LanguageServerIndicator.Hidden);
            return;
        }

        if (!_registry.IsEnabled(target.Launch.WorkspaceFolder, target.Launch.Definition))
        {
            Detach();
            _target = target;
            SetIndicator(new LanguageServerIndicator(
                LanguageServerIndicatorState.Off,
                target.Launch.Definition.Command,
                OutsideWorkspace: target.Launch.WorkspaceFolder.Length == 0));
            return;
        }

        if (_lease is not null && target == _target)
        {
            return;
        }

        Detach();
        _target = target;
        _lease = _registry.Acquire(target.Launch);
        _lease.Instance.StatusChanged += Instance_StatusChanged;
        ApplyInstanceStatus();
    }

    /// <summary>Turns the document's server on or off for its workspace folder.</summary>
    public void Toggle()
    {
        if (_target is not { } target || Indicator.State == LanguageServerIndicatorState.Hidden)
        {
            return;
        }

        _registry.SetEnabled(
            target.Launch.WorkspaceFolder,
            target.Launch.Definition,
            Indicator.State == LanguageServerIndicatorState.Off);
    }

    /// <summary>
    /// Looks again after a file was opened or read from disk, which cleared
    /// the window's problems: a document the server already had is opened
    /// anew so that the server reports them again.
    /// </summary>
    public void Reloaded()
    {
        var previous = _active;
        Update();
        if (_active is { } active && ReferenceEquals(active, previous) && _target is { } target)
        {
            Observe(active.Client.ReopenAsync(active.Uri, target.LanguageId, _editor.Text));
        }
    }

    /// <summary>Tells the server the document was saved, after the session became clean.</summary>
    public void Saved()
    {
        if (_active is { } active)
        {
            Observe(active.Client.SaveAsync(active.Uri, _editor.Text));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _editor.Edited -= Editor_Edited;
        _registry.EnablementChanged -= Registry_EnablementChanged;
        _changeTimer.Dispose();
        Detach();
    }

    /// <summary>
    /// Finds the workspace folder and the server's program, which searches the
    /// disk, so the answer is kept until the file, mode or definition changes.
    /// </summary>
    private Target? ResolveTarget()
    {
        if (_definition() is not { } definition || _state().FilePath is not { } filePath)
        {
            return null;
        }

        var modeId = _languageModeId();
        var workspacePatterns = _workspacePatterns();
        if (_resolved is { } resolved
            && resolved.FilePath == filePath
            && resolved.ModeId == modeId
            && ReferenceEquals(resolved.Definition, definition)
            && ReferenceEquals(resolved.Patterns, workspacePatterns))
        {
            return resolved.Target;
        }

        var target = ResolveTarget(filePath, modeId, definition, workspacePatterns);
        _resolved = (filePath, modeId, definition, workspacePatterns, target);
        return target;
    }

    private static Target? ResolveTarget(
        string filePath,
        string modeId,
        LanguageServerDefinition definition,
        IReadOnlyList<string> workspacePatterns)
    {
        // A file outside every workspace folder gets the server only when the
        // definition allows it, as the empty folder all such files share.
        var workspaceFolder = WorkspaceFolderResolver.FindForFile(filePath, null, workspacePatterns)
            ?? (definition.AllowOutsideWorkspace ? string.Empty : null);
        if (workspaceFolder is null)
        {
            return null;
        }

        var context = new ExternalToolContext(
            filePath,
            filePath,
            string.Empty,
            string.Empty,
            languageId: modeId,
            toolDirectory: definition.DefinitionDirectory,
            workspacePatterns: workspacePatterns);
        var environment = ExternalToolEnvironmentResolver.LoadProcessEnvironment();
        var searchPath = ExternalToolLaunchResolver.ExpandSearchPath(
            definition.SearchPath,
            definition.DefinitionDirectory,
            context,
            environment);
        var command = context.Expand(definition.Command, environment);
        if (string.IsNullOrWhiteSpace(command)
            || ExternalToolLaunchResolver.Resolve(
                command,
                ExternalToolCommandMode.Executable,
                definition.DefinitionDirectory,
                searchPath) is not { } plan)
        {
            return null;
        }

        var arguments = definition.Arguments.Select(argument => context.Expand(argument, environment)).ToArray();
        return new Target(
            new LanguageServerLaunch(definition, plan, arguments, workspaceFolder),
            LanguageServerClient.ToUri(filePath),
            definition.LanguageId ?? modeId);
    }

    private void Detach()
    {
        _changeTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (_active is { } active)
        {
            _active = null;
            active.Client.DiagnosticsPublished -= Client_DiagnosticsPublished;
            Observe(active.Client.CloseAsync(active.Uri));
        }

        if (_lease is { } lease)
        {
            _lease = null;
            lease.Instance.StatusChanged -= Instance_StatusChanged;
            lease.Dispose();
        }

        _diagnostics.Replace(DiagnosticsSourceKey, []);
    }

    private void ApplyInstanceStatus()
    {
        if (_lease is not { } lease || _target is not { } target)
        {
            return;
        }

        var instance = lease.Instance;
        var command = target.Launch.Definition.Command;
        switch (instance.Status)
        {
            case LanguageServerStatus.Running when instance.Client is { } client:
                var opened = !ReferenceEquals(_active?.Client, client);
                if (opened)
                {
                    _active = new Active(client, target.Uri);
                    client.DiagnosticsPublished += Client_DiagnosticsPublished;
                    Observe(client.OpenAsync(target.Uri, target.LanguageId, _editor.Text));
                }

                // A new server can bring other completion triggers.
                SetIndicator(new LanguageServerIndicator(LanguageServerIndicatorState.Running, command), force: opened);
                break;
            case LanguageServerStatus.Failed:
                if (_active is { } failed)
                {
                    _active = null;
                    failed.Client.DiagnosticsPublished -= Client_DiagnosticsPublished;
                }

                _diagnostics.Replace(DiagnosticsSourceKey, []);
                SetIndicator(new LanguageServerIndicator(
                    LanguageServerIndicatorState.Failed,
                    command,
                    instance.FailureMessage));
                break;
            default:
                SetIndicator(new LanguageServerIndicator(LanguageServerIndicatorState.Starting, command));
                break;
        }
    }

    private void SetIndicator(LanguageServerIndicator indicator, bool force = false)
    {
        if (!force && indicator == Indicator)
        {
            return;
        }

        Indicator = indicator;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SendChange()
    {
        if (_active is { } active)
        {
            Observe(active.Client.ChangeAsync(active.Uri, _editor.Text));
        }
    }

    private void Editor_Edited(object? sender, TextChange change)
    {
        if (_active is not null)
        {
            _changeTimer.Change(ChangeDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Registry_EnablementChanged(object? sender, EventArgs args) =>
        _dispatcher.TryEnqueue(Update);

    private void Instance_StatusChanged(object? sender, EventArgs args) =>
        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed && ReferenceEquals(sender, _lease?.Instance))
            {
                ApplyInstanceStatus();
            }
        });

    private void Client_DiagnosticsPublished(LanguageServerDiagnostics published) =>
        _dispatcher.TryEnqueue(() =>
        {
            if (_active is not { } active
                || !string.Equals(published.Uri, active.Uri, StringComparison.Ordinal)
                || _editor.Snapshot is not { } snapshot
                || !string.Equals(snapshot.Text, published.Text, StringComparison.Ordinal))
            {
                // A newer copy of the document is on its way to the server,
                // and the problems for it will follow.
                return;
            }

            _diagnostics.Replace(
                DiagnosticsSourceKey,
                LanguageServerConversions.ToDiagnostics(
                    published.Diagnostics,
                    snapshot,
                    DiagnosticsSourceKey,
                    _target?.Launch.Definition.Command ?? "language server"));
        });

    private static void Observe(Task task) =>
        _ = task.ContinueWith(
            completed => ErrorReporter.LogException("Language server", completed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private sealed record Target(LanguageServerLaunch Launch, string Uri, string LanguageId)
    {
        public bool Equals(Target? other) =>
            other is not null
            && Launch.Key == other.Launch.Key
            && Launch.Arguments.SequenceEqual(other.Launch.Arguments)
            && Uri == other.Uri
            && LanguageId == other.LanguageId;

        public override int GetHashCode() => HashCode.Combine(Launch.Key, Uri, LanguageId);
    }

    private sealed record Active(LanguageServerClient Client, string Uri);

    private sealed class CompletionProvider(LanguageServerController owner, ICompletionProvider? fallback)
        : ICompletionProvider
    {
        public async ValueTask<CompletionResult?> GetCompletionsAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default)
        {
            if (owner._active is not { Client.SupportsCompletion: true } active)
            {
                return fallback is null
                    ? null
                    : await fallback.GetCompletionsAsync(context, cancellationToken).ConfigureAwait(false);
            }

            var result = await RequestAsync(
                active,
                context,
                (client, uri, position, token) => client.CompletionAsync(uri, position, token),
                cancellationToken).ConfigureAwait(false);
            return LanguageServerConversions.ToCompletionResult(result, context);
        }
    }

    private sealed class HoverProvider(LanguageServerController owner) : ITooltipProvider
    {
        public async ValueTask<TooltipData?> GetTooltipAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default)
        {
            if (owner._active is not { Client.SupportsHover: true } active)
            {
                return null;
            }

            var result = await RequestAsync(
                active,
                context,
                (client, uri, position, token) => client.HoverAsync(uri, position, token),
                cancellationToken).ConfigureAwait(false);
            return LanguageServerConversions.ToTooltip(result, context);
        }
    }

    /// <summary>
    /// Sends the text the editor asks about before the request, so the server
    /// answers for that text. A server that fails a request answers nothing.
    /// </summary>
    private static async Task<JsonNode?> RequestAsync(
        Active active,
        EditorProviderContext context,
        Func<LanguageServerClient, string, LineColumn, CancellationToken, Task<JsonNode?>> request,
        CancellationToken cancellationToken)
    {
        try
        {
            await active.Client.ChangeAsync(active.Uri, context.Snapshot.Text).ConfigureAwait(false);
            return await request(active.Client, active.Uri, context.Location, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or JsonRpcException or ObjectDisposedException)
        {
            return null;
        }
    }
}
