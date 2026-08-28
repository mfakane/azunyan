namespace Azunote;

internal sealed class SettingsWorkflow : IDisposable
{
    private readonly SettingsController _settings;
    private readonly LanguageModeController _languageModes;
    private readonly IExternalToolMenuView _externalToolsMenu;
    private readonly IFileChangeMonitorFactory _monitorFactory;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUserPrompt _prompt;
    private readonly ISettingsFolderOpener _folderOpener;
    private readonly Func<string?> _currentFilePath;
    private readonly Func<ExternalToolSettings, Task> _runConfiguredTool;
    private readonly Func<ExternalToolSettings, ExternalToolMenuState> _getToolState;
    private readonly Func<string, Task> _editDefinition;
    private readonly Func<string, Task> _showDefinitionInExplorer;
    private IReadOnlyList<ExternalToolMenuNode> _externalToolMenu = [];
    private IFileChangeMonitor? _settingsMonitor;
    private bool _disposed;

    public SettingsWorkflow(
        SettingsController settings,
        LanguageModeController languageModes,
        IExternalToolMenuView externalToolsMenu,
        IFileChangeMonitorFactory monitorFactory,
        IUiDispatcher dispatcher,
        IUserPrompt prompt,
        ISettingsFolderOpener folderOpener,
        Func<string?> currentFilePath,
        Func<ExternalToolSettings, Task> runConfiguredTool,
        Func<ExternalToolSettings, ExternalToolMenuState>? getToolState = null,
        Func<string, Task>? editDefinition = null,
        Func<string, Task>? showDefinitionInExplorer = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _languageModes = languageModes ?? throw new ArgumentNullException(nameof(languageModes));
        _externalToolsMenu = externalToolsMenu ?? throw new ArgumentNullException(nameof(externalToolsMenu));
        _monitorFactory = monitorFactory ?? throw new ArgumentNullException(nameof(monitorFactory));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _folderOpener = folderOpener ?? throw new ArgumentNullException(nameof(folderOpener));
        _currentFilePath = currentFilePath ?? throw new ArgumentNullException(nameof(currentFilePath));
        _runConfiguredTool = runConfiguredTool ?? throw new ArgumentNullException(nameof(runConfiguredTool));
        _getToolState = getToolState ?? (_ => new ExternalToolMenuState(true, true));
        _editDefinition = editDefinition ?? (_ => Task.CompletedTask);
        _showDefinitionInExplorer = showDefinitionInExplorer ?? (_ => Task.CompletedTask);
    }

    public AzunoteSettings Current => _settings.Current;

    public async Task InitializeAsync()
    {
        try
        {
            await _settings.EnsureExistsAsync();
            await ReloadAsync(showError: true);
            StartWatcher();
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not initialize settings", exception.Message);
        }
    }

    public async Task OpenPreferencesAsync()
    {
        try
        {
            await _settings.EnsureExistsAsync();
            await _folderOpener.OpenAsync(_settings.Directory);
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not open settings folder", exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopWatcher();
    }

    public void RefreshExternalToolsMenu() => RenderExternalTools();

    private async Task<bool> ReloadAsync(bool showError)
    {
        try
        {
            var settings = await _settings.LoadAsync();
            _languageModes.Initialize(settings.CustomSyntaxModes);
            if (!_languageModes.IsManuallySelected
                && _currentFilePath() is { } path)
            {
                _languageModes.SelectForPath(path);
            }

            _externalToolMenu = settings.ExternalToolMenu;
            RenderExternalTools();

            return true;
        }
        catch (Exception exception)
        {
            if (showError)
            {
                await _prompt.ShowErrorAsync("Could not load settings", exception.Message);
            }

            return false;
        }
    }

    private void RenderExternalTools() =>
        _externalToolsMenu.Render(
            _externalToolMenu,
            _getToolState,
            _runConfiguredTool,
            _editDefinition,
            _showDefinitionInExplorer);

    private void StartWatcher()
    {
        StopWatcher();
        if (_disposed)
        {
            return;
        }

        var directory = Path.GetFullPath(_settings.Directory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        _settingsMonitor = _monitorFactory.Create(directory, includeSubdirectories: true);
        _settingsMonitor.Changed += SettingsMonitor_Changed;
    }

    private void StopWatcher()
    {
        if (_settingsMonitor is null)
        {
            return;
        }

        _settingsMonitor.Changed -= SettingsMonitor_Changed;
        _settingsMonitor.Dispose();
        _settingsMonitor = null;
    }

    private void SettingsMonitor_Changed(
        object? sender,
        FileChangeDetectedEventArgs args)
    {
        if (ReferenceEquals(sender, _settingsMonitor)
            && !IsApplicationStateChange(args.ChangedPath))
        {
            _dispatcher.TryEnqueue(() => _ = HandleSettingsChangedAsync());
        }
    }

    private bool IsApplicationStateChange(string? changedPath) =>
        changedPath is not null
        && string.Equals(
            Path.GetFullPath(changedPath),
            StateFileService.GetStateFilePath(_settings.Directory),
            StringComparison.OrdinalIgnoreCase);

    private async Task HandleSettingsChangedAsync()
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            await _settings.EnsureExistsAsync();
            await ReloadAsync(showError: true);
        }
        catch (OperationCanceledException)
        {
            // A burst of settings notifications was superseded by a newer one.
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not reload settings", exception.Message);
        }
    }
}
