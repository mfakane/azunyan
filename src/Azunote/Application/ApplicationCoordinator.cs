namespace Azunote;

internal sealed class ApplicationCoordinator : IDisposable
{
    private readonly WindowRegistry<WindowRegistration> _windows = new();
    private bool _disposed;

    public MainWindow? Window => _windows.Active?.Window;

    public void Launch(AzunoteCommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, nameof(ApplicationCoordinator));

        var runtime = CreateWindow().Runtime;

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

    internal Task CreateNewDocumentWindowAsync()
    {
        CreateWindow();
        return Task.CompletedTask;
    }

    internal async Task OpenFileInNewWindowAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var window = CreateWindow();
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

        _windows.Unregister(registration);
        RefreshWindowMenus();
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
            registration.Window.Dispose();
            _windows.Unregister(registration);
        }
    }

    private MainWindow CreateWindow()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ApplicationCoordinator));

        var window = new MainWindow(this);
        _windows.Register(new WindowRegistration(window));
        window.Activate();
        window.FocusEditor();
        _ = window.Runtime.InitializeSettingsAsync();
        RefreshWindowMenus();
        return window;
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
    }
}
