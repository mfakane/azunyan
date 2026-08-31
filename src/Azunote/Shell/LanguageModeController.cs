using Azunyan.Core;
using Azunyan.Syntax;

namespace Azunote;

internal sealed class LanguageModeController
{
    private readonly IEditorView _editor;
    private readonly ILanguageModeMenuView _menu;
    private readonly Func<string?> _currentFilePath;
    private readonly Func<string, Task> _editDefinition;
    private readonly Func<string, Task> _showDefinitionInExplorer;
    private LanguageModeCatalog _catalog = LanguageModeCatalog.Create();
    private string _currentModeId = "plain-text";
    private bool _manuallySelected;

    public LanguageModeController(
        IEditorView editor,
        ILanguageModeMenuView menu,
        Func<string?> currentFilePath,
        Func<string, Task>? editDefinition = null,
        Func<string, Task>? showDefinitionInExplorer = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _menu = menu ?? throw new ArgumentNullException(nameof(menu));
        _currentFilePath = currentFilePath ?? throw new ArgumentNullException(nameof(currentFilePath));
        _editDefinition = editDefinition ?? (_ => Task.CompletedTask);
        _showDefinitionInExplorer = showDefinitionInExplorer ?? (_ => Task.CompletedTask);
    }

    public string CurrentModeId => _currentModeId;

    public string CurrentModeDisplayName =>
        _catalog.TryGet(_currentModeId, out var mode)
            ? mode.DisplayName
            : _currentModeId;

    public bool IsManuallySelected => _manuallySelected;

    public event EventHandler? Changed;

    public IReadOnlyList<FileDialogFilter> GetFileDialogFilters() =>
        _catalog.GetFileDialogFilters();

    public void Initialize(IReadOnlyList<SyntaxLanguageDefinition>? customModes = null)
    {
        var selectedMode = _currentModeId;
        _catalog = LanguageModeCatalog.Create(customModes);
        _menu.Render(
            _catalog.Entries,
            _catalog.CustomModeStartIndex,
            SelectManually,
            _editDefinition,
            _showDefinitionInExplorer);
        if (!_catalog.TryGet(selectedMode, out _))
        {
            selectedMode = "plain-text";
        }

        Apply(selectedMode, refresh: customModes is not null);
    }

    public void SelectManually(string id)
    {
        _manuallySelected = true;
        Apply(id, refresh: true);
    }

    public void DocumentOpened(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _manuallySelected = false;
        SelectForPath(path);
    }

    public void SelectForPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_manuallySelected)
        {
            return;
        }

        var selectedModeId = _catalog.SelectForPath(path);
        if (!string.Equals(
            _currentModeId,
            selectedModeId,
            StringComparison.OrdinalIgnoreCase))
        {
            Apply(selectedModeId, refresh: true);
        }
    }

    private void Apply(string id, bool refresh)
    {
        if (!_catalog.TryGet(id, out var mode))
        {
            return;
        }

        _currentModeId = id;
        var isAzunote = string.Equals(id, "azunote", StringComparison.OrdinalIgnoreCase);
        var schemas = isAzunote
            ? AzunoteSchemaCatalog.ForPath(_currentFilePath())
            : Array.Empty<AzunoteSchemaDefinition>();
        _editor.ApplyLanguage(new EditorLanguageConfiguration(
            mode.CompletionTriggers,
            isAzunote ? new AzunoteSyntaxProvider() : mode.Provider,
            isAzunote ? new AzunoteCompletionProvider(schemas) : null));
        _menu.Select(id);
        if (refresh)
        {
            _editor.RefreshProviders();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
