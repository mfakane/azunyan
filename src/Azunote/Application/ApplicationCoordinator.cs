using Microsoft.UI.Dispatching;

namespace Azunote;

internal sealed class ApplicationCoordinator : IDisposable
{
    private readonly WindowRegistry<WindowRegistration> _windows = new();
    private readonly ApplicationStateController _state =
        new(SettingsFileService.GetDefaultDirectory());
    private DispatcherQueue? _dispatcherQueue;
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
            _ = OpenStandardInputAsync(runtime, options);
        }
        else if (!string.IsNullOrWhiteSpace(options.FilePath))
        {
            _ = runtime.OpenStartupDocumentAsync(
                options.FilePath,
                options.Line,
                options.Column);
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
                var registration = CreateWindowRegistration();
                var path = ResolvePath(options.FilePath, command.WorkingDirectory);
                await registration.Window.Runtime.OpenStartupDocumentAsync(
                    path,
                    options.Line,
                    options.Column);
                await WaitForCloseIfRequestedAsync(registration, options);
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

        _windows.Unregister(registration);
        registration.MarkClosed();
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
        var entries = _windows.Windows
            .Select(registration => new WindowMenuEntry(
                registration.Id,
                registration.Window.DocumentName,
                ReferenceEquals(registration, _windows.Active)))
            .ToArray();

        foreach (var registration in _windows.Windows.ToArray())
        {
            registration.Window.RenderWindowMenu(
                entries,
                registration.Window.IsAlwaysOnTop,
                ActivateWindowById);
        }
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

    private WindowRegistration CreateWindowRegistration()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ApplicationCoordinator));

        var window = new MainWindow(this);
        if (_state.IsInitialized)
        {
            var state = _state.Current;
            window.ApplyWindowSize(state.Window);
            window.ApplyViewState(state);
        }

        var registration = new WindowRegistration(window);
        _windows.Register(registration);
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
