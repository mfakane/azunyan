using Microsoft.UI.Input;
using Azunyan.Core;
using Azunyan.WinUI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace Azunote;

public sealed partial class MainWindow : Window, IDisposable, IMainWindowActions
{
    private const VirtualKey OemBackslashKey = (VirtualKey)0xdc;
    private const VirtualKey OemOpenBracketKey = (VirtualKey)0xdb;
    private const VirtualKey OemSlashKey = (VirtualKey)0xbf;

    private readonly ApplicationCoordinator _application;
    private readonly MainWindowRuntime _runtime;
    private AppWindow? _appWindow;
    private bool _allowClose;
    private ExternalToolProposal? _proposal;
    private bool _proposalUpdatePending;
    private static readonly Dictionary<ExternalToolShortcut, Action<MainWindowRuntime>> EditorCommandShortcuts = new()
    {
        [new(VirtualKey.Up, VirtualKeyModifiers.Menu)] = runtime => runtime.MoveLines(down: false),
        [new(VirtualKey.Down, VirtualKeyModifiers.Menu)] = runtime => runtime.MoveLines(down: true),
        [new(VirtualKey.Up, VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift)] =
            runtime => runtime.DuplicateLines(down: false),
        [new(VirtualKey.Down, VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift)] =
            runtime => runtime.DuplicateLines(down: true),
        [new(VirtualKey.K, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift)] =
            runtime => runtime.DeleteLines(),
        [new(VirtualKey.D, VirtualKeyModifiers.Control)] = runtime => runtime.AddNextOccurrence(),
        [new(OemSlashKey, VirtualKeyModifiers.Control)] = runtime => runtime.ToggleComment(),
    };

    private bool _completionShortcutInvoked;
    private bool _duplicateWindowShortcutInvoked;
    private bool _matchingBracketShortcutInvoked;
    private bool _findNextShortcutInvoked;
    private bool _findPreviousShortcutInvoked;
    private bool _disposed;

    internal MainWindowRuntime Runtime => _runtime;
    internal MainWindowViewModel ViewModel { get; } = new();

    internal MainWindow(
        ApplicationCoordinator application,
        DocumentSession? session = null,
        Document? document = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        InitializeComponent();
        InitializeView();
        ConfigureTheme();
        _runtime = new MainWindowRuntime(
            this,
            ViewModel,
            application.CreateNewDocumentWindowAsync,
            application.OpenFileInNewWindowAsync,
            application.OpenTextInNewWindowAsync,
            application.RefreshWindowMenus,
            new WinUiFilePathActions(),
            application.RecordRecentFile,
            application.RecordWordWrap,
            application.RecordStatusBarVisible,
            application.Backups,
            session,
            document,
            path => application.OpenFileAsync(this, path),
            application.OpenStreamedTextInNewWindowAsync,
            proposal => application.OpenProposalAsync(this, proposal),
            application.LanguageServers);
        ViewModel.Attach(this);
        // MenuBarItem does not expose its internal flyout's Opening event.
        // Refresh synchronously on entry so the flyout's first paint is final.
        ToolsMenuItem.PointerEntered += (_, _) => _runtime.RefreshExternalToolsMenuNow();
        ToolsMenuItem.PointerPressed += (_, _) => _runtime.RefreshExternalToolsMenuNow();
        ToolsMenuItem.GotFocus += (_, _) => _runtime.RefreshExternalToolsMenuNow();
        ToolsMenuItem.KeyDown += (_, _) => _runtime.RefreshExternalToolsMenuNow();
        Editor.ContextMenuOpening += (_, _) => _runtime.RefreshExternalToolsMenuNow();
        RegisterKeyboardAccelerators();
        WatchDocumentSelection();

        if (_appWindow is not null)
        {
            _appWindow.Closing += AppWindow_Closing;
        }

        Closed += MainWindow_Closed;
        Activated += MainWindow_Activated;
    }

    private void RegisterKeyboardAccelerators()
    {
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += EscapeAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(escape);

        var duplicateWindow = new KeyboardAccelerator
        {
            Key = OemBackslashKey,
            Modifiers = VirtualKeyModifiers.Control
        };
        duplicateWindow.Invoked += DuplicateWindowAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(duplicateWindow);

        var nextWindow = new KeyboardAccelerator
        {
            Key = VirtualKey.Tab,
            Modifiers = VirtualKeyModifiers.Control
        };
        nextWindow.Invoked += NextWindowAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(nextWindow);

        var previousWindow = new KeyboardAccelerator
        {
            Key = VirtualKey.Tab,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift
        };
        previousWindow.Invoked += PreviousWindowAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(previousWindow);

        var goToMatchingBracket = new KeyboardAccelerator
        {
            Key = OemOpenBracketKey,
            Modifiers = VirtualKeyModifiers.Control
        };
        goToMatchingBracket.Invoked += GoToMatchingBracketAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(goToMatchingBracket);

        foreach (var (key, modifiers) in new[]
                 {
                     (VirtualKey.P, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
                     (VirtualKey.F1, VirtualKeyModifiers.None)
                 })
        {
            var showCommandPalette = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            showCommandPalette.Invoked += ShowCommandPaletteAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(showCommandPalette);
        }

        // The native text input handles arrow keys before window accelerators,
        // so shortcuts that the editor would otherwise consume are resolved
        // through the editor's own key pipeline.
        Editor.HostKeyCommand = ResolveEditorKeyCommand;

        var findNext = new KeyboardAccelerator { Key = VirtualKey.F3 };
        findNext.Invoked += FindNextAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(findNext);

        var findPrevious = new KeyboardAccelerator
        {
            Key = VirtualKey.F3,
            Modifiers = VirtualKeyModifiers.Shift
        };
        findPrevious.Invoked += FindPreviousAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(findPrevious);
    }

    private void IndentationStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ShowIndentationSizeMenu();

    private void EncodingStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ShowEncodingMenu();

    private void LineEndingStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ShowLineEndingMenu();

    private void FilePathStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ShowFilePathMenu();

    private async void PositionStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        await _runtime.ShowGoToLineAsync();

    private void ShowCompletionAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _completionShortcutInvoked = true;
        _runtime.ShowCompletion();
    }

    private void GoToMatchingBracketAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _matchingBracketShortcutInvoked = true;
        _runtime.MoveToMatchingBracket();
    }

    /// <summary>
    /// Gives an enabled external tool precedence over the editor for its
    /// shortcut, and binds the editing commands when no such tool is enabled.
    /// </summary>
    private EditorKeyCommand? ResolveEditorKeyCommand(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        var shortcut = new ExternalToolShortcut(key, modifiers);
        if (_externalToolShortcuts.TryGetValue(shortcut, out var resolveTool)
            && resolveTool() is { } runTool)
        {
            return new EditorKeyCommand(runTool, Repeatable: false);
        }

        return EditorCommandShortcuts.TryGetValue(shortcut, out var command)
            ? new EditorKeyCommand(() => command(_runtime), Repeatable: true)
            : null;
    }

    private void ShowCommandPaletteAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        // An enabled external tool keeps its shortcut, as in the editor.
        if (_externalToolShortcuts.TryGetValue(new(sender.Key, sender.Modifiers), out var resolveTool)
            && resolveTool() is { } runTool)
        {
            runTool();
            return;
        }

        ShowCommandPalette(CommandPalettePrefix);
    }

    private void FindNextAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _findNextShortcutInvoked = true;
        _runtime.FindNext();
    }

    private void FindPreviousAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _findPreviousShortcutInvoked = true;
        _runtime.FindPrevious();
    }

    private void EscapeAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsCommandPaletteOpen)
        {
            args.Handled = true;
            CloseCommandPalette(focusEditor: true);
        }
        else if (_runtime.IsFindPanelVisible)
        {
            args.Handled = true;
            _runtime.CloseFindPanel();
        }
    }

    private void DuplicateWindowAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _duplicateWindowShortcutInvoked = true;
        _application.DuplicateWindow(this);
    }

    private void NextWindowAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _application.CycleWindow(this, direction: 1);
    }

    private void PreviousWindowAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _application.CycleWindow(this, direction: -1);
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !NeedsCloseConfirmation())
        {
            return;
        }

        args.Cancel = true;
        if (!await _runtime.TryConfirmCloseAsync())
        {
            return;
        }

        _runtime.DiscardBackup();
        _allowClose = true;
        Close();
    }

    /// <summary>
    /// Closes the window, asking about unsaved changes as closing it from its
    /// title bar would. Returns false when the user cancels.
    /// </summary>
    internal async Task<bool> TryCloseAsync()
    {
        if (!_allowClose && NeedsCloseConfirmation())
        {
            if (!await _runtime.TryConfirmCloseAsync())
            {
                return false;
            }

            _runtime.DiscardBackup();
        }

        _allowClose = true;
        Close();
        return true;
    }

    /// <summary>
    /// Returns false when the window can close as it is: its document is
    /// saved, still shown in another window, or kept as a backup to restore
    /// on the next launch.
    /// </summary>
    private bool NeedsCloseConfirmation()
    {
        if (!_runtime.IsDirty || _application.HasOtherView(this))
        {
            return false;
        }

        if (!_application.CanCloseKeepingBackup(this))
        {
            return true;
        }

        _runtime.KeepBackup();
        return false;
    }

    private void MainWindow_Activated(
        object sender,
        Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        ApplyWindowGroupAccentCore();
        if (_isWindowActive)
        {
            _application.WindowActivated(this);
        }
    }

    /// <summary>Shows this window's diff as a change to apply or discard.</summary>
    internal void ShowProposal(ExternalToolProposal proposal)
    {
        _proposal = proposal;
        proposal.Changed += Proposal_Changed;
        ShowProposalState(proposal);
        ProposalBar.IsOpen = true;
        FollowProposalDocument();
    }

    // Edits to the document change the diff; one update follows a burst of them.
    private void Proposal_Changed(object? sender, EventArgs e)
    {
        if (_proposalUpdatePending)
        {
            return;
        }

        _proposalUpdatePending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _proposalUpdatePending = false;
            if (_proposal is { } proposal)
            {
                _updatingProposalDiff = true;
                try
                {
                    _runtime.ReplaceReadOnlyText(proposal.Diff);
                }
                finally
                {
                    _updatingProposalDiff = false;
                }

                ShowProposalState(proposal);
                FollowProposalDocument();
            }
        });
    }

    private void ShowProposalState(ExternalToolProposal proposal)
    {
        var conflicts = proposal.ConflictCount;
        ProposalBar.Severity = conflicts > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        ProposalBar.Message = proposal.IsEmpty
            ? "Every proposed change overlaps an edit, so none is left to apply."
            : conflicts > 0
                ? $"{proposal.Title} {conflicts} change{(conflicts == 1 ? " that overlaps an edit is" : "s that overlap edits are")} left out."
                : proposal.Title;
        ApplyProposalButton.IsEnabled = !proposal.IsEmpty;
    }

    private void ApplyProposalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_proposal is not { } proposal)
        {
            return;
        }

        if (proposal.Apply() is { } problem)
        {
            ProposalBar.Severity = InfoBarSeverity.Error;
            ProposalBar.Message = problem;
            return;
        }

        proposal.Changed -= Proposal_Changed;
        _proposal = null;
        CloseWithoutAsking();
    }

    private void DiscardProposalButton_Click(object sender, RoutedEventArgs e) => CloseWithoutAsking();

    /// <summary>Closes the window, leaving whatever it holds unsaved.</summary>
    internal void CloseWithoutAsking()
    {
        _allowClose = true;
        _runtime.DiscardBackup();
        Close();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // The document can outlive this window when another one shows it.
        if (_watchedDocument is not null)
        {
            _watchedDocument.SelectionChanged -= Document_SelectionChanged;
        }

        if (_proposal is { } proposal)
        {
            proposal.Changed -= Proposal_Changed;
            proposal.Discard();
        }

        _application.WindowClosed(this);
        Dispose();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e) =>
        _runtime.ObserveTextChanged();

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        using var measurement = ShellPerformance.Measure("selection.changed");
        _runtime.RefreshStatus();
    }

    private void Document_SelectionChanged(object? sender, EventArgs e)
    {
        // A proposal's diff and its document select the same text. Only the
        // window being worked in passes its selection on, so the selection
        // one window receives is not passed back.
        if (!_isWindowActive || _updatingProposalDiff)
        {
            return;
        }

        if (_proposal is { } proposal)
        {
            proposal.SelectInDocument(proposal.ToDocument(Document.Selection));
        }
        else
        {
            _application.SelectionChanged(this);
        }
    }

    private bool _followPending;

    // Set while this window changes its diff's text or selection itself, so
    // those changes are not passed to the document as if the user made them.
    private bool _updatingProposalDiff;

    /// <summary>
    /// Selects in this proposal's diff what is selected in its document, once
    /// the document's editor is done with what moved its selection.
    /// </summary>
    internal void QueueFollowProposalDocument()
    {
        if (_followPending)
        {
            return;
        }

        _followPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _followPending = false;
            FollowProposalDocument();
        });
    }

    private void FollowProposalDocument()
    {
        if (_proposal?.ToDiff(_proposal.DocumentSelection) is { } selection)
        {
            _updatingProposalDiff = true;
            try
            {
                Editor.SetDocumentSelection(selection);
                Editor.ScrollSelectionIntoView();
            }
            finally
            {
                _updatingProposalDiff = false;
            }
        }
    }

    private void Editor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (HandleFindNavigationKeyDown(e))
        {
            return;
        }

        if (e.Key == OemBackslashKey
            && IsKeyDown(VirtualKey.Control)
            && !IsKeyDown(VirtualKey.Menu))
        {
            var acceleratorInvoked = _duplicateWindowShortcutInvoked;
            _duplicateWindowShortcutInvoked = false;
            e.Handled = true;
            if (!acceleratorInvoked)
            {
                // WinUI does not consistently invoke KeyboardAccelerator for
                // OEM punctuation keys. Keep this fallback in the shell so
                // the editor component remains unaware of the application shortcut.
                _application.DuplicateWindow(this);
            }

            return;
        }

        if (e.Key == OemOpenBracketKey
            && IsKeyDown(VirtualKey.Control)
            && !IsKeyDown(VirtualKey.Menu))
        {
            var acceleratorInvoked = _matchingBracketShortcutInvoked;
            _matchingBracketShortcutInvoked = false;
            e.Handled = true;
            if (!acceleratorInvoked)
            {
                // WinUI does not consistently invoke KeyboardAccelerator for
                // OEM punctuation keys. Keep this fallback in the shell so the
                // editor component remains unaware of the application shortcut.
                _runtime.MoveToMatchingBracket();
            }

            return;
        }

        if (e.Key == VirtualKey.Space && IsKeyDown(VirtualKey.Control))
        {
            var acceleratorInvoked = _completionShortcutInvoked;
            _completionShortcutInvoked = false;
            e.Handled = true;
            if (!acceleratorInvoked)
            {
                _runtime.ShowCompletion();
            }

            return;
        }

        if (e.Key == VirtualKey.Enter
            && _runtime.IsFindPanelVisible
            && _runtime.IsFindBoxFocused)
        {
            _runtime.FindNext();
            e.Handled = true;
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(CoreVirtualKeyStates.Down);

    private void Editor_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void Editor_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.FirstOrDefault(item => item is StorageFile);
        if (file is not null)
        {
            _runtime.OpenDroppedFile(file.Path);
        }
    }

    private void FindReplaceModeButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.ToggleFindReplaceMode();

    private void FindPreviousButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.FindPrevious();

    private void FindNextButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.FindNext();

    private void CloseFindButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.CloseFindPanel();

    private void FindTextBox_BeforeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleFindTextBoxKeyDown(FindTextBox, _runtime.FindNext, e);
    }

    private void ReplaceTextBox_BeforeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleFindTextBoxKeyDown(ReplaceTextBox, _runtime.ReplaceCurrent, e);
    }

    private void HandleFindTextBoxKeyDown(
        TextBox textBox,
        Action submit,
        KeyRoutedEventArgs args)
    {
        if (HandleFindNavigationKeyDown(args))
        {
            return;
        }

        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        if (IsKeyDown(VirtualKey.Control) && !IsKeyDown(VirtualKey.Menu))
        {
            InsertFindTextNewLine(textBox);
        }
        else
        {
            submit();
        }
    }

    private void ExecuteTextEditingCommand(
        Action editorCommand,
        Action<TextBox> textBoxCommand)
    {
        if (FindTextBox.FocusState != FocusState.Unfocused)
        {
            textBoxCommand(FindTextBox);
            return;
        }

        if (ReplaceTextBox.FocusState != FocusState.Unfocused)
        {
            textBoxCommand(ReplaceTextBox);
            return;
        }

        if (CommandPaletteTextBox.FocusState != FocusState.Unfocused)
        {
            textBoxCommand(CommandPaletteTextBox);
            return;
        }

        editorCommand();
    }

    private static void CutTextBoxSelection(TextBox textBox)
    {
        if (textBox.SelectionLength == 0)
        {
            return;
        }

        CopyTextBoxSelection(textBox);
        ReplaceTextBoxSelection(textBox, string.Empty);
    }

    private static void CopyTextBoxSelection(TextBox textBox)
    {
        if (textBox.SelectionLength == 0)
        {
            return;
        }

        var selectedText = textBox.Text.Substring(
            textBox.SelectionStart,
            textBox.SelectionLength);
        var dataPackage = new DataPackage();
        dataPackage.SetText(selectedText);
        Clipboard.SetContent(dataPackage);
    }

    private static async void PasteIntoTextBox(TextBox textBox)
    {
        try
        {
            var dataView = Clipboard.GetContent();
            if (!dataView.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var text = await dataView.GetTextAsync();
            ReplaceTextBoxSelection(textBox, text);
        }
        catch (Exception exception)
        {
            ErrorReporter.LogException("Find/replace clipboard paste", exception);
        }
    }

    private static void ReplaceTextBoxSelection(TextBox textBox, string replacement)
    {
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        textBox.Text = textBox.Text.Remove(selectionStart, selectionLength)
            .Insert(selectionStart, replacement);
        textBox.SelectionStart = selectionStart + replacement.Length;
        textBox.SelectionLength = 0;
    }

    private static void InsertFindTextNewLine(TextBox textBox)
    {
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        textBox.Text = textBox.Text.Remove(selectionStart, selectionLength)
            .Insert(selectionStart, "\r");
        textBox.SelectionStart = selectionStart + 1;
        textBox.SelectionLength = 0;
    }

    private bool HandleFindNavigationKeyDown(KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.F3) return false;
        var previous = IsKeyDown(VirtualKey.Shift);
        var acceleratorInvoked = previous
            ? _findPreviousShortcutInvoked
            : _findNextShortcutInvoked;
        _findNextShortcutInvoked = false;
        _findPreviousShortcutInvoked = false;
        args.Handled = true;
        if (!acceleratorInvoked)
        {
            if (previous) _runtime.FindPrevious();
            else _runtime.FindNext();
        }
        return true;
    }

    Task IMainWindowActions.OpenFileAsync() => _runtime.OpenFileAsync();

    Task IMainWindowActions.NewDocumentAsync() => _runtime.NewDocumentAsync();

    async Task IMainWindowActions.SaveAsync() => await _runtime.SaveAsync();

    async Task IMainWindowActions.SaveAsAsync() => await _runtime.SaveAsAsync();

    void IMainWindowActions.Exit() => _ = _application.CloseAllWindowsAsync();

    void IMainWindowActions.Undo() => _runtime.Undo();

    void IMainWindowActions.Redo() => _runtime.Redo();

    void IMainWindowActions.Cut() => ExecuteTextEditingCommand(
        _runtime.Cut,
        CutTextBoxSelection);

    void IMainWindowActions.Copy() => ExecuteTextEditingCommand(
        _runtime.Copy,
        CopyTextBoxSelection);

    void IMainWindowActions.Paste() => ExecuteTextEditingCommand(
        _runtime.Paste,
        PasteIntoTextBox);

    void IMainWindowActions.SelectAll() => ExecuteTextEditingCommand(
        _runtime.SelectAll,
        textBox => textBox.SelectAll());

    void IMainWindowActions.GoToMatchingBracket() => _runtime.MoveToMatchingBracket();

    void IMainWindowActions.GoToProblem(bool next) => _runtime.GoToProblem(next);

    void IMainWindowActions.ShowProblems() => _runtime.ShowProblems();

    private void ProblemsStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ShowProblems();

    private void LanguageModeStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        ShowLanguageModePicker();

    private void LanguageServerStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ToggleLanguageServer();

    void IMainWindowActions.MoveLines(bool down) => _runtime.MoveLines(down);

    void IMainWindowActions.DuplicateLines(bool down) => _runtime.DuplicateLines(down);

    void IMainWindowActions.DeleteLines() => _runtime.DeleteLines();

    void IMainWindowActions.AddNextOccurrence() => _runtime.AddNextOccurrence();

    void IMainWindowActions.ToggleComment() => _runtime.ToggleComment();

    void IMainWindowActions.ReopenWithEncoding(TextEncoding encoding) =>
        _runtime.ReopenWithEncoding(encoding);

    void IMainWindowActions.SaveWithEncoding(TextEncoding encoding) =>
        _runtime.SaveWithEncoding(encoding);

    void IMainWindowActions.ShowEncodingPicker(bool reopen) =>
        _runtime.ShowEncodingPicker(reopen);

    void IMainWindowActions.ChangeLineEnding(LineEndingKind lineEnding) =>
        _runtime.ChangeLineEnding(lineEnding);

    void IMainWindowActions.ShowFind() => _runtime.ShowFindPanel(replace: false);

    void IMainWindowActions.ShowReplace() => _runtime.ShowFindPanel(replace: true);

    void IMainWindowActions.ShowCompletion() => _runtime.ShowCompletion();

    Task IMainWindowActions.GoToLineAsync() => _runtime.ShowGoToLineAsync();

    void IMainWindowActions.ToggleWordWrap() => _runtime.ToggleWordWrap();

    void IMainWindowActions.SetTabDisplaySize(int size) => _runtime.SetTabDisplaySize(size);

    void IMainWindowActions.SetIndentSize(int? size) => _runtime.SetIndentSize(size);

    void IMainWindowActions.SetIndentationInputMode(IndentationInputMode mode) =>
        _runtime.SetIndentationInputMode(mode);

    void IMainWindowActions.ToggleStatusBar() => _runtime.ToggleStatusBar();

    void IMainWindowActions.CopyFilePath() => _runtime.CopyFilePath();

    Task IMainWindowActions.ShowFileInExplorerAsync() => _runtime.ShowFileInExplorerAsync();

    Task IMainWindowActions.OpenFolderInTerminalAsync() => _runtime.OpenFolderInTerminalAsync();

    Task IMainWindowActions.RunExternalToolAsync() => _runtime.ShowExternalToolDialogAsync();

    Task IMainWindowActions.OpenPreferencesAsync() => _runtime.OpenPreferencesAsync();

    void IMainWindowActions.ToggleAlwaysOnTop()
    {
        _runtime.ToggleAlwaysOnTop();
        _application.RefreshWindowMenus();
    }

    void IMainWindowActions.DuplicateWindow() => _application.DuplicateWindow(this);

    void IMainWindowActions.ShowAllWindows() => _application.ShowAllWindows();

    void IMainWindowActions.MinimizeAllWindows() => _application.MinimizeAllWindows();

    void IMainWindowActions.NextWindow() => _application.CycleWindow(this, direction: 1);

    void IMainWindowActions.PreviousWindow() => _application.CycleWindow(this, direction: -1);

    Task IMainWindowActions.ShowAboutAsync() => _runtime.ShowAboutAsync();

    Task IMainWindowActions.ViewLicenseAsync() => _runtime.ViewLicenseAsync();

    Task IMainWindowActions.ShowThirdPartyNoticesAsync() => _runtime.ShowThirdPartyNoticesAsync();

    void IMainWindowActions.FindNext() => _runtime.FindNext();

    void IMainWindowActions.FindTextChanged() => _runtime.OnFindTextChanged();

    void IMainWindowActions.FindOptionsChanged() => _runtime.OnFindOptionsChanged();

    void IMainWindowActions.ReplaceCurrent() => _runtime.ReplaceCurrent();

    void IMainWindowActions.ReplaceAll() => _runtime.ReplaceAll();

    void IMainWindowActions.CloseFind() => _runtime.CloseFindPanel();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WindowSnapper.Remove(this);
        _runtime.Dispose();
        DisposeView();
    }

    internal string DocumentName => _runtime.DocumentName;

    internal bool IsAlwaysOnTop => IsAlwaysOnTopView;

    internal WindowLayoutState? WindowSize => WindowSizeView;

    internal void ApplyWindowSize(WindowLayoutState size) => ApplyWindowSizeView(size);

    internal void ApplyViewState(AzunoteState state) => _runtime.ApplyViewState(state);

    internal void RenderRecentFiles(IReadOnlyList<string> paths) =>
        _runtime.RenderRecentFiles(paths, _application.RemoveRecentFile);

    internal void ActivateWindow()
    {
        RestoreIfMinimizedView();
        Activate();
        WindowForeground.BringToFront(WindowHandle);
    }

    internal void MinimizeWindow() => MinimizeView();

    internal void EnableWindowSnapping() => WindowSnapper.Install(this);

    internal void SetWindowSnappingEnabled(bool enabled) =>
        WindowSnapper.SetEnabled(this, enabled);

    internal WindowSnapBounds SnapWindow(
        WindowSnapBounds bounds,
        WindowSnapEdges resizingEdges = WindowSnapEdges.None) =>
        _application.SnapWindow(this, bounds, resizingEdges);

    internal void RestoreIfMinimized() => RestoreIfMinimizedView();

    internal void FocusEditor() => Focus();

    internal void RenderWindowMenu(
        IReadOnlyList<WindowMenuEntry> entries,
        bool isAlwaysOnTop,
        Action<string> onSelected) =>
        RenderWindowMenuCore(entries, isAlwaysOnTop, onSelected);

}
