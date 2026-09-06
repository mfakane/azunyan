using Microsoft.UI.Dispatching;

namespace Azunote;

internal sealed class ApplicationCoordinator : IDisposable
{
    private static readonly IEqualityComparer<DocumentSession> DocumentSessionComparer =
        ReferenceEqualityComparer.Instance;
    private readonly WindowRegistry<WindowRegistration> _windows = new();
    private readonly Dictionary<DocumentSession, int> _windowGroupAccentIndices =
        new(DocumentSessionComparer);
    private readonly ApplicationStateController _state =
        new(SettingsFileService.GetDefaultDirectory());
    private DispatcherQueue? _dispatcherQueue;
    private int _nextWindowGroupAccentIndex;
    private bool _disposed;

    public MainWindow? Window => _windows.Active?.Window;

    public void Launch(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ObjectDisposedException.ThrowIf(_disposed, nameof(ApplicationCoordinator));

        var registration = CreateWindowRegistration();
        _dispatcherQueue = registration.Window.DispatcherQueue;
        _ = ProcessInitialCommandLineAsync(registration, arguments);
    }

    internal Task HandleForwardedCommandLineAsync(SingleInstanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(_disposed, nameof(ApplicationCoordinator));

        var dispatcher = _dispatcherQueue
            ?? throw new InvalidOperationException("The application has not launched.");
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (dispatcher.HasThreadAccess)
        {
            _ = ProcessForwardedCommandLineAsync(command, completion);
        }
        else if (!dispatcher.TryEnqueue(() =>
            _ = ProcessForwardedCommandLineAsync(command, completion)))
        {
            completion.SetException(
                new InvalidOperationException("The Azunote UI dispatcher is unavailable."));
        }

        return completion.Task;
    }

    internal void ActivateActiveWindow()
    {
        _windows.Active?.Window.ActivateWindow();
    }

    private async Task ProcessInitialCommandLineAsync(
        WindowRegistration registration,
        IReadOnlyList<string> arguments)
    {
        await _state.InitializeAsync().ConfigureAwait(true);
        if (!registration.IsClosed)
        {
            var state = _state.Current;
            registration.Window.ApplyWindowSize(state.Window);
            registration.Window.ApplyViewState(state);
        }
        RefreshRecentFileMenus();

        var options = ParseCommandLine(arguments);
        var runtime = registration.Window.Runtime;

        if (options.ReadStandardInput)
        {
            await OpenStandardInputAsync(runtime, options);
            await WaitForCloseIfRequestedAsync(registration, options);
        }
        else if (!string.IsNullOrWhiteSpace(options.FilePath))
        {
            await runtime.OpenStartupDocumentAsync(
                options.FilePath,
                options.Line,
                options.Column);
            await WaitForCloseIfRequestedAsync(registration, options);
        }
        else if (options.ShowHelp)
        {
            _ = runtime.ShowCommandLineHelpAsync();
        }
    }

    private async Task ProcessForwardedCommandLineAsync(
        SingleInstanceCommand command,
        TaskCompletionSource<object?> completion)
    {
        try
        {
            await _state.InitializeAsync().ConfigureAwait(true);
            RefreshRecentFileMenus();
            var options = ParseCommandLine(command.Arguments);
            ActivateActiveWindow();

            if (options.ReadStandardInput)
            {
                var registration = CreateWindowRegistration();
                await registration.Window.Runtime.OpenStartupTextAsync(
                    command.StandardInput ?? string.Empty,
                    options.Line,
                    options.Column);
                await WaitForCloseIfRequestedAsync(registration, options);
            }
            else if (!string.IsNullOrWhiteSpace(options.FilePath))
            {
                var path = ResolvePath(options.FilePath, command.WorkingDirectory);
                var existing = FindWindowForFile(path);
                if (existing is not null)
                {
                    existing.Window.ActivateWindow();
                    await WaitForCloseIfRequestedAsync(existing, options);
                }
                else
                {
                    var registration = CreateWindowRegistration();
                    await registration.Window.Runtime.OpenStartupDocumentAsync(
                        path,
                        options.Line,
                        options.Column);
                    await WaitForCloseIfRequestedAsync(registration, options);
                }
            }
            else if (options.ShowHelp && _windows.Active is { } active)
            {
                await active.Window.Runtime.ShowCommandLineHelpAsync();
            }

            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    internal Task CreateNewDocumentWindowAsync()
    {
        CreateWindowRegistration();
        return Task.CompletedTask;
    }

    internal async Task OpenFileInNewWindowAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var window = CreateWindowRegistration().Window;
        await window.Runtime.OpenStartupDocumentAsync(path);
    }

    internal async Task OpenFileAsync(MainWindow source, string path)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var existing = FindWindowForFile(path);
        if (existing is not null)
        {
            existing.Window.ActivateWindow();
            return;
        }

        var sourceRegistration = FindRegistration(source);
        if (sourceRegistration is null)
        {
            return;
        }

        if (source.Runtime.Session.State.FilePath is null && !source.Runtime.IsDirty)
        {
            await source.Runtime.OpenStartupDocumentAsync(path);
        }
        else
        {
            await OpenFileInNewWindowAsync(path);
        }
    }

    internal void DuplicateWindow(MainWindow source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var registration = FindRegistration(source);
        if (registration is null) return;

        CreateWindowRegistration(
            source.Runtime.Session,
            source.Runtime.CreateDocumentView());
    }

    internal bool HasOtherView(MainWindow source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _windows.Windows.Any(registration =>
            !ReferenceEquals(registration.Window, source)
            && ReferenceEquals(registration.Window.Runtime.Session, source.Runtime.Session));
    }

    internal void CycleWindow(MainWindow source, int direction)
    {
        ArgumentNullException.ThrowIfNull(source);
        var registration = FindRegistration(source);
        if (registration is null)
        {
            return;
        }

        var next = _windows.CycleFrom(registration, direction);
        next?.Window.ActivateWindow();
    }

    internal void ShowAllWindows()
    {
        foreach (var registration in _windows.Windows.ToArray())
        {
            registration.Window.RestoreIfMinimized();
        }
    }

    internal void MinimizeAllWindows()
    {
        foreach (var registration in _windows.Windows.ToArray())
        {
            registration.Window.MinimizeWindow();
        }
    }

    internal void WindowActivated(MainWindow window)
    {
        var registration = FindRegistration(window);
        if (registration is null)
        {
            return;
        }

        _windows.MarkActive(registration);
        RefreshWindowMenus();
    }

    internal void WindowClosed(MainWindow window)
    {
        var registration = FindRegistration(window);
        if (registration is null)
        {
            return;
        }

        if (window.WindowSize is { } windowSize)
        {
            _state.RecordWindowSize(windowSize);
        }

        var session = window.Runtime.Session;
        _windows.Unregister(registration);
        registration.MarkClosed();
        var remainingViews = _windows.Windows.Where(candidate =>
            ReferenceEquals(candidate.Window.Runtime.Session, session)).ToArray();
        if (!remainingViews.Any(candidate => candidate.Window.Runtime.IsFileWatcherActive))
        {
            remainingViews.FirstOrDefault()?.Window.Runtime.EnsureFileWatcher();
        }
        RefreshWindowMenus();
    }

    internal void RecordRecentFile(string path)
    {
        _state.RecordRecentFile(path);
        RefreshRecentFileMenus();
    }

    internal void RemoveRecentFile(string path)
    {
        if (_state.RemoveRecentFile(path))
        {
            RefreshRecentFileMenus();
        }
    }

    internal void RecordWordWrap(bool enabled)
    {
        _state.RecordWordWrap(enabled);
        foreach (var registration in _windows.Windows.ToArray())
        {
            registration.Window.Runtime.ApplyViewState(_state.Current);
        }
    }

    internal void RecordStatusBarVisible(bool visible)
    {
        _state.RecordStatusBarVisible(visible);
        foreach (var registration in _windows.Windows.ToArray())
        {
            registration.Window.Runtime.ApplyViewState(_state.Current);
        }
    }

    internal void RefreshWindowMenus()
    {
        var registrations = _windows.Windows.ToArray();
        var entries = new List<WindowMenuEntry>(registrations.Length);
        var activeSessions = new HashSet<DocumentSession>(DocumentSessionComparer);

        foreach (var group in registrations.GroupBy(
                     registration => registration.Window.Runtime.Session,
                     DocumentSessionComparer))
        {
            var groupEntries = group.ToArray();
            var isDuplicateGroup = groupEntries.Length > 1;
            var accentIndex = isDuplicateGroup
                ? GetWindowGroupAccentIndex(group.Key)
                : (int?)null;
            activeSessions.Add(group.Key);

            for (var index = 0; index < groupEntries.Length; index++)
            {
                var registration = groupEntries[index];
                registration.Window.SetWindowGroupAccent(accentIndex);
                entries.Add(new WindowMenuEntry(
                    registration.Id,
                    registration.Window.DocumentName,
                    ReferenceEquals(registration, _windows.Active),
                    index == 0,
                    isDuplicateGroup));
            }
        }

        foreach (var session in _windowGroupAccentIndices.Keys.ToArray())
        {
            if (!activeSessions.Contains(session))
            {
                _windowGroupAccentIndices.Remove(session);
            }
        }

        foreach (var registration in registrations)
        {
            registration.Window.RenderWindowMenu(
                entries,
                registration.Window.IsAlwaysOnTop,
                ActivateWindowById);
        }
    }

    private int GetWindowGroupAccentIndex(DocumentSession session)
    {
        if (_windowGroupAccentIndices.TryGetValue(session, out var index))
        {
            return index;
        }

        index = _nextWindowGroupAccentIndex++ % MainWindow.WindowGroupAccentPaletteSize;
        _windowGroupAccentIndices.Add(session, index);
        return index;
    }

    public bool TryShowUnhandledError(Exception exception, string logPath)
    {
        if (Window is null)
        {
            return false;
        }

        Window.Runtime.ShowUnhandledError(exception, logPath);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var registration in _windows.Windows.ToArray())
        {
            if (registration.Window.WindowSize is { } windowSize)
            {
                _state.RecordWindowSize(windowSize);
            }

            registration.Window.Dispose();
            registration.MarkClosed();
            _windows.Unregister(registration);
        }

        _state.Dispose();
    }

    private WindowRegistration CreateWindowRegistration(
        DocumentSession? session = null,
        Azunyan.Core.Document? document = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ApplicationCoordinator));

        var window = new MainWindow(this, session, document);
        if (_state.IsInitialized)
        {
            var state = _state.Current;
            window.ApplyWindowSize(state.Window);
            window.ApplyViewState(state);
        }

        var registration = new WindowRegistration(window);
        _windows.Register(registration);
        _windows.MarkActive(registration);
        window.Activate();
        window.FocusEditor();
        _ = window.Runtime.InitializeSettingsAsync();
        window.RenderRecentFiles(_state.Current.RecentFiles);
        RefreshWindowMenus();
        return registration;
    }

    private void RefreshRecentFileMenus()
    {
        var recentFiles = _state.Current.RecentFiles;
        foreach (var registration in _windows.Windows.ToArray())
        {
            registration.Window.RenderRecentFiles(recentFiles);
        }
    }

    private void ActivateWindowById(string id)
    {
        var registration = _windows.Windows.FirstOrDefault(
            candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        registration?.Window.ActivateWindow();
    }

    private WindowRegistration? FindRegistration(MainWindow window) =>
        _windows.Windows.FirstOrDefault(
            registration => ReferenceEquals(registration.Window, window));

    private WindowRegistration? FindWindowForFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        static bool HasPath(WindowRegistration registration, string path) =>
            registration.Window.Runtime.Session.State.FilePath is { } openPath
            && string.Equals(openPath, path, StringComparison.OrdinalIgnoreCase);

        if (_windows.Active is { } active && HasPath(active, fullPath))
        {
            return active;
        }

        return _windows.Windows.FirstOrDefault(registration =>
            HasPath(registration, fullPath));
    }

    private static AzunoteCommandLineOptions ParseCommandLine(
        IReadOnlyList<string> arguments)
    {
        try
        {
            return AzunoteCommandLine.Parse(arguments);
        }
        catch (CommandLineParseException exception)
        {
            ErrorReporter.LogMessage("Invalid command line", exception.Message);
            return new AzunoteCommandLineOptions { ShowHelp = true };
        }
    }

    private static string ResolvePath(string path, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, workingDirectory);
    }

    private static async Task WaitForCloseIfRequestedAsync(
        WindowRegistration registration,
        AzunoteCommandLineOptions options)
    {
        if (options.WaitForExit)
        {
            await registration.Closed.ConfigureAwait(true);
        }
    }

    private static async Task OpenStandardInputAsync(
        MainWindowRuntime runtime,
        AzunoteCommandLineOptions options)
    {
        try
        {
            var text = await Console.In.ReadToEndAsync();
            await runtime.OpenStartupTextAsync(text, options.Line, options.Column);
        }
        catch (Exception exception)
        {
            await runtime.ShowStartupErrorAsync("Could not read standard input", exception.Message);
        }
    }

    private sealed class WindowRegistration
    {
        public WindowRegistration(MainWindow window)
        {
            Window = window ?? throw new ArgumentNullException(nameof(window));
            Id = Guid.NewGuid().ToString("N");
        }

        public string Id { get; }

        public MainWindow Window { get; }

        public Task Closed => _closed.Task;

        public bool IsClosed => _closed.Task.IsCompleted;

        public void MarkClosed() => _closed.TrySetResult(null);

        private readonly TaskCompletionSource<object?> _closed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
