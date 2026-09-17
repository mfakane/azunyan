namespace Azunote;

/// <summary>
/// Shows one window what <see cref="SettingsService"/> has read. The reading
/// itself is shared: this renders it into the window's menus and editor.
/// </summary>
internal sealed class SettingsWorkflow : IDisposable
{
    private readonly SettingsService _service;
    private readonly LanguageModeController _languageModes;
    private readonly IExternalToolMenuView _externalToolsMenu;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUserPrompt _prompt;
    private readonly ISettingsFolderOpener _folderOpener;
    private readonly Func<string?> _currentFilePath;
    private readonly Func<ExternalToolSettings, Task> _runConfiguredTool;
    private readonly Func<ExternalToolSettings, ExternalToolMenuState> _getToolState;
    private readonly Func<string, Task> _editDefinition;
    private readonly Func<string, Task> _showDefinitionInExplorer;
    private readonly Action<AzunoteSettings> _applySettings;
    private IReadOnlyList<ExternalToolMenuNode> _externalToolMenu = [];
    private AzunoteSettings _current = new();
    private SettingsSnapshot? _applied;
    private bool _subscribed;
    private bool _disposed;
    private readonly Action? _requestToolRefresh;
    internal IReadOnlyList<ExternalToolMenuNode> ExternalToolMenu => _externalToolMenu;
    internal IReadOnlyDictionary<ExternalToolSettings, PreparedExternalTool> PreparedTools { get; private set; }
        = new Dictionary<ExternalToolSettings, PreparedExternalTool>();

    public SettingsWorkflow(
        SettingsService service,
        LanguageModeController languageModes,
        IExternalToolMenuView externalToolsMenu,
        IUiDispatcher dispatcher,
        IUserPrompt prompt,
        ISettingsFolderOpener folderOpener,
        Func<string?> currentFilePath,
        Func<ExternalToolSettings, Task> runConfiguredTool,
        Func<ExternalToolSettings, ExternalToolMenuState>? getToolState = null,
        Func<string, Task>? editDefinition = null,
        Func<string, Task>? showDefinitionInExplorer = null,
        Action<AzunoteSettings>? applySettings = null,
        Action? requestToolRefresh = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _languageModes = languageModes ?? throw new ArgumentNullException(nameof(languageModes));
        _externalToolsMenu = externalToolsMenu ?? throw new ArgumentNullException(nameof(externalToolsMenu));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _folderOpener = folderOpener ?? throw new ArgumentNullException(nameof(folderOpener));
        _currentFilePath = currentFilePath ?? throw new ArgumentNullException(nameof(currentFilePath));
        _runConfiguredTool = runConfiguredTool ?? throw new ArgumentNullException(nameof(runConfiguredTool));
        _getToolState = getToolState ?? (_ => new ExternalToolMenuState(true, true));
        _editDefinition = editDefinition ?? (_ => Task.CompletedTask);
        _showDefinitionInExplorer = showDefinitionInExplorer ?? (_ => Task.CompletedTask);
        _applySettings = applySettings ?? (_ => { });
        _requestToolRefresh = requestToolRefresh;
    }

    public AzunoteSettings Current => _current;

    public async Task InitializeAsync()
    {
        try
        {
            // Subscribing first: a reading that lands before the one below is
            // read arrives as an event instead, and neither is missed.
            _service.Loaded += Service_Loaded;
            _service.LoadFailed += Service_LoadFailed;
            _subscribed = true;
            await _service.EnsureInitializedAsync();
            if (_service.Current is { } snapshot)
            {
                Apply(snapshot);
            }
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
            await _service.EnsureExistsAsync();
            await _folderOpener.OpenAsync(_service.Directory);
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
        if (_subscribed)
        {
            _service.Loaded -= Service_Loaded;
            _service.LoadFailed -= Service_LoadFailed;
            _subscribed = false;
        }
    }

    public void RefreshExternalToolsMenu()
    {
        if (_requestToolRefresh is { } request) request();
        else RenderExternalTools();
    }

    internal void ApplyExternalToolStates(IReadOnlyDictionary<ExternalToolSettings, ExternalToolMenuState> states) =>
        _externalToolsMenu.Render(_externalToolMenu, tool => states[tool],
            _runConfiguredTool, _editDefinition, _showDefinitionInExplorer);

    private void Service_Loaded(object? sender, SettingsSnapshot snapshot) =>
        _dispatcher.TryEnqueue(() => Apply(snapshot));

    private void Service_LoadFailed(object? sender, Exception exception) =>
        _dispatcher.TryEnqueue(() => _ = _prompt.ShowErrorAsync(
            "Could not load settings",
            exception.Message));

    /// <summary>
    /// Renders one reading into this window. The same reading can arrive both
    /// as an event and as the current one when the window joins, so applying
    /// it twice has to be free.
    /// </summary>
    private void Apply(SettingsSnapshot snapshot)
    {
        if (_disposed || ReferenceEquals(_applied, snapshot))
        {
            return;
        }

        _applied = snapshot;
        _current = snapshot.Settings;
        _applySettings(snapshot.Settings);
        _languageModes.Initialize(snapshot.Settings.CustomSyntaxModes);
        if (!_languageModes.IsManuallySelected
            && _currentFilePath() is { } path)
        {
            _languageModes.SelectForPath(path);
        }

        _externalToolMenu = snapshot.Settings.ExternalToolMenu;
        PreparedTools = snapshot.PreparedTools;
        RefreshExternalToolsMenu();
    }

    private void RenderExternalTools() =>
        _externalToolsMenu.Render(
            _externalToolMenu,
            _getToolState,
            _runConfiguredTool,
            _editDefinition,
            _showDefinitionInExplorer);
}
