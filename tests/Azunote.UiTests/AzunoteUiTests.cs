using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Xunit;
using Xunit.Sdk;

namespace Azunote.UiTests;

/// <summary>
/// Windows UI Automation smoke tests for the unpackaged Azunote desktop app.
/// These tests are opt-in because they need an unlocked interactive desktop.
/// </summary>
public sealed class AzunoteUiTests : IClassFixture<AzunoteUiFixture>
{
    private readonly AzunoteUiFixture _fixture;

    public AzunoteUiTests(AzunoteUiFixture fixture)
    {
        _fixture = fixture;
    }

    [AzunoteUiFact]
    public void Main_window_starts_without_initialization_exception()
    {
        Assert.NotNull(_fixture.Editor);
    }

    [AzunoteUiFact]
    public void Projected_editor_exposes_snapshot_text_and_visible_ranges()
    {
        var editor = _fixture.Editor;
        var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
        var text = AzunoteUiFixture.WaitForDocumentText(
            textPattern,
            expected => expected.Contains("日本語", StringComparison.Ordinal));

        Assert.Contains("Azunyan UI verification", text);
        Assert.Contains("😀", text);

        var visibleRanges = textPattern.GetVisibleRanges();
        Assert.NotEmpty(visibleRanges);
        Assert.Contains(
            visibleRanges,
            range => range.GetText(-1).Length > 0);

        var rectangles = visibleRanges
            .SelectMany(range => range.GetBoundingRectangles())
            .ToArray();
        Assert.NotEmpty(rectangles);
        Assert.True(
            rectangles.Any(rect => rect.Width > 0 && rect.Height > 0),
            $"Expected visible geometry, got [{string.Join(", ", rectangles)}].");

        var editorBounds = editor.Current.BoundingRectangle;
        var firstTextBounds = rectangles
            .Where(rect => rect.Width > 0 && rect.Height > 0)
            .OrderBy(rect => rect.Top)
            .First();
        var topInset = firstTextBounds.Top - editorBounds.Top;
        Assert.InRange(topInset, 0, 64);
    }

    [AzunoteUiFact]
    public void Text_range_can_find_and_select_projected_document_text()
    {
        var editor = _fixture.Editor;
        var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
        var documentRange = textPattern.DocumentRange;
        var targetRange = documentRange.FindText("日本語", false, false);
        Assert.NotNull(targetRange);

        targetRange!.Select();
        var selectedText = AzunoteUiFixture.WaitForSelectionText(
            textPattern,
            expected => string.Equals(expected, "日本語", StringComparison.Ordinal));
        Assert.Equal("日本語", selectedText);
    }

    [AzunoteUiFact]
    public void Fold_gutter_chevron_invokes_toml_section_toggle()
    {
        var editor = _fixture.Editor;
        var valuePattern = AzunoteUiFixture.WaitForValuePattern(editor);
        var original = valuePattern.Current.Value;
        const string toml = "[server]\nname = \"Azunote\"\nport = 8080\n";

        try
        {
            valuePattern.SetValue(toml);
            var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            AzunoteUiFixture.WaitForDocumentText(
                textPattern,
                text => string.Equals(text, toml, StringComparison.Ordinal));

            var collapseButton = AzunoteUiFixture.WaitForElement(
                editor,
                AutomationElement.NameProperty,
                "Collapse section");
            Assert.Equal(ControlType.Button, collapseButton.Current.ControlType);
            ((InvokePattern)collapseButton.GetCurrentPattern(InvokePattern.Pattern))
                .Invoke();

            var expandButton = AzunoteUiFixture.WaitForElement(
                editor,
                AutomationElement.NameProperty,
                "Expand section");
            Assert.Equal(ControlType.Button, expandButton.Current.ControlType);
            ((InvokePattern)expandButton.GetCurrentPattern(InvokePattern.Pattern))
                .Invoke();

            AzunoteUiFixture.WaitForElement(
                editor,
                AutomationElement.NameProperty,
                "Collapse section");
        }
        finally
        {
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
        }
    }

    [AzunoteUiFact]
    public void Find_and_replace_modes_are_exposed()
    {
        var window = _fixture.Window;
        var editor = _fixture.Editor;
        var editorValue = AzunoteUiFixture.WaitForValuePattern(editor);
        var original = editorValue.Current.Value;

        try
        {
            editorValue.SetValue("one two ONE");
            var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            AzunoteUiFixture.WaitForDocumentText(
                textPattern,
                text => string.Equals(text, "one two ONE", StringComparison.Ordinal));

            var first = textPattern.DocumentRange.FindText("one", false, false);
            Assert.NotNull(first);
            first!.Select();

            AzunoteUiFixture.InvokeMenuItem(window, "Edit", "Find...");
            var modeButton = AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                "Toggle Replace fields");

            var findText = AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                "Find text");
            ((ValuePattern)findText.GetCurrentPattern(ValuePattern.Pattern)).SetValue("one");

            var matchCase = AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                "Match Case");
            AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                "Match Whole Word");
            AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                "Regular Expression");
            var matchCaseToggle = (TogglePattern)matchCase.GetCurrentPattern(TogglePattern.Pattern);
            Assert.Equal(ToggleState.Off, matchCaseToggle.Current.ToggleState);
            matchCaseToggle.Toggle();
            AzunoteUiFixture.WaitForToggleState(matchCase, ToggleState.On);
            matchCaseToggle.Toggle();
            AzunoteUiFixture.WaitForToggleState(matchCase, ToggleState.Off);

            var modeButtonInvoke = (InvokePattern)modeButton.GetCurrentPattern(InvokePattern.Pattern);
            modeButtonInvoke.Invoke();
            AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                "Replace text");

            modeButtonInvoke.Invoke();
            AzunoteUiFixture.WaitForElementHidden(
                window,
                AutomationElement.NameProperty,
                "Replace text");

            AzunoteUiFixture.InvokeMenuItem(window, "Edit", "Replace...");
            AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                "Replace text");
        }
        finally
        {
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
            AzunoteUiFixture.TryCloseFindReplace(window);
        }
    }

    [AzunoteUiFact]
    public void Projected_editor_exposes_read_write_value_and_empty_range_geometry()
    {
        var editor = _fixture.Editor;
        var valuePattern = AzunoteUiFixture.WaitForValuePattern(editor);
        var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
        var original = valuePattern.Current.Value;
        var replacement = original + "\nUIA value update";

        try
        {
            valuePattern.SetValue(replacement);

            var updatedTextPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            var updatedText = AzunoteUiFixture.WaitForDocumentText(
                updatedTextPattern,
                text => string.Equals(text, replacement, StringComparison.Ordinal));
            Assert.Equal(replacement, updatedText);

            var updatedValuePattern = AzunoteUiFixture.WaitForValuePattern(
                editor,
                value => string.Equals(value, replacement, StringComparison.Ordinal));
            Assert.Equal(replacement, updatedValuePattern.Current.Value);

            var emptyRange = textPattern.DocumentRange.Clone();
            emptyRange.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                emptyRange,
                TextPatternRangeEndpoint.Start);
            Assert.NotEmpty(emptyRange.GetBoundingRectangles());
        }
        finally
        {
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
        }
    }

    [AzunoteUiFact]
    public void Projected_editor_does_not_expose_native_edit_control_as_duplicate()
    {
        var editor = _fixture.Editor;
        Assert.Equal(ControlType.Edit, editor.Current.ControlType);

        var nestedEditors = editor.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Edit));
        Assert.Empty(nestedEditors);
    }

    [AzunoteUiFact]
    public void Projected_editor_keeps_long_document_text_in_the_document_owner()
    {
        var editor = _fixture.Editor;
        var valuePattern = AzunoteUiFixture.WaitForValuePattern(editor);
        var original = valuePattern.Current.Value;
        var replacement = string.Concat(
            new string('a', 3000),
            "日本語😀",
            new string('b', 3000));

        try
        {
            valuePattern.SetValue(replacement);

            var updatedTextPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            var updatedText = AzunoteUiFixture.WaitForDocumentText(
                updatedTextPattern,
                text => string.Equals(text, replacement, StringComparison.Ordinal));
            Assert.Equal(replacement, updatedText);
        }
        finally
        {
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
        }
    }

    [AzunoteUiFact]
    public void Undo_and_redo_keep_the_projected_editor_alive_after_value_update()
    {
        var editor = _fixture.Editor;
        var window = _fixture.Window;
        var valuePattern = AzunoteUiFixture.WaitForValuePattern(editor);
        var original = valuePattern.Current.Value;
        var replacement = original + "\nUndo/redo UI verification";

        try
        {
            valuePattern.SetValue(replacement);
            AzunoteUiFixture.WaitForValuePattern(
                editor,
                value => string.Equals(value, replacement, StringComparison.Ordinal));

            AzunoteUiFixture.InvokeMenuItem(window, "Edit", "Undo");
            AzunoteUiFixture.WaitForValuePattern(
                editor,
                value => string.Equals(value, original, StringComparison.Ordinal));

            AzunoteUiFixture.InvokeMenuItem(window, "Edit", "Redo");
            var restored = AzunoteUiFixture.WaitForValuePattern(
                editor,
                value => string.Equals(value, replacement, StringComparison.Ordinal));
            Assert.Equal(replacement, restored.Current.Value);
        }
        finally
        {
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
        }
    }

    [AzunoteUiFact]
    public void Projected_range_remains_bound_to_old_snapshot_after_value_update()
    {
        var editor = _fixture.Editor;
        var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
        var valuePattern = AzunoteUiFixture.WaitForValuePattern(editor);
        var oldRange = textPattern.DocumentRange.FindText("日本語", false, false);
        Assert.NotNull(oldRange);

        var original = valuePattern.Current.Value;
        var replacement = original.Replace(
            "日本語",
            "置換後の日本語",
            StringComparison.Ordinal);

        try
        {
            valuePattern.SetValue(replacement);
            var updatedTextPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            AzunoteUiFixture.WaitForDocumentText(
                updatedTextPattern,
                text => string.Equals(text, replacement, StringComparison.Ordinal));

            Assert.Equal("日本語", oldRange!.GetText(-1));
            Assert.Empty(oldRange.GetBoundingRectangles());
        }
        finally
        {
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
        }
    }

    [AzunoteUiFact]
    public void New_file_creates_a_window_and_window_menu_lists_all_instances()
    {
        var originalWindow = _fixture.Window;
        AzunoteUiFixture.InvokeMenuItem(originalWindow, "File", "New");

        var windows = _fixture.WaitForWindows(current => current.Length >= 2);
        Assert.True(windows.Length >= 2);

        AzunoteUiFixture.OpenMenu(originalWindow, "Window");
        AzunoteUiFixture.WaitFor(
            () => originalWindow.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.NameProperty,
                    "Untitled")).Count >= 1
                ? originalWindow
                : null,
            "The Window menu did not list the new Untitled window.");
    }

    [AzunoteUiFact]
    public void Duplicate_window_keeps_one_current_window_checked_in_its_window_menu()
    {
        var window = _fixture.Window;
        var originalCount = _fixture.WaitForWindows(_ => true).Length;
        AzunoteUiFixture.InvokeMenuItem(window, "Window", "Duplicate Window");
        _fixture.WaitForWindows(current => current.Length > originalCount);

        window.SetFocus();
        AzunoteUiFixture.OpenMenu(window, "Window");
        AzunoteUiFixture.WaitFor(
            () =>
            {
                var selectedItems = window.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.MenuItem))
                    .Cast<AutomationElement>()
                    .Count(item => item.TryGetCurrentPattern(
                            SelectionItemPattern.Pattern,
                            out var pattern)
                        && pattern is SelectionItemPattern selection
                        && selection.Current.IsSelected);
                return selectedItems == 1 ? window : null;
            },
            "The active window did not have exactly one checked document in the Window menu.");
    }
}

public sealed class AzunoteUiFixture : IDisposable
{
    private const string EnabledVariable = "AZUNOTE_UI_TESTS";
    private const string ExecutableVariable = "AZUNOTE_EXE";
    private Process? _process;
    private AutomationElement? _window;

    public AutomationElement Editor
    {
        get
        {
            EnsureStarted();
            return FindRequired(
                _window!,
                AutomationElement.NameProperty,
                "Document editor");
        }
    }

    public AutomationElement Window
    {
        get
        {
            EnsureStarted();
            return _window!;
        }
    }

    public static TextPattern WaitForTextPattern(AutomationElement editor)
    {
        return WaitFor(
            () => editor.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)
                && pattern is TextPattern textPattern
                    ? textPattern
                    : null,
            "The projected editor did not expose TextPattern.");
    }

    public static ValuePattern WaitForValuePattern(
        AutomationElement editor,
        Func<string, bool>? predicate = null)
    {
        predicate ??= _ => true;
        return WaitFor(
            () =>
            {
                if (!editor.TryGetCurrentPattern(
                        ValuePattern.Pattern,
                        out var pattern)
                    || pattern is not ValuePattern valuePattern)
                {
                    return null;
                }

                try
                {
                    return predicate(valuePattern.Current.Value)
                        ? valuePattern
                        : null;
                }
                catch (ElementNotAvailableException)
                {
                    return null;
                }
            },
            "The projected editor did not expose the expected ValuePattern value.");
    }

    public static string WaitForDocumentText(
        TextPattern textPattern,
        Func<string, bool> predicate)
    {
        return WaitFor(
            () =>
            {
                try
                {
                    var text = textPattern.DocumentRange.GetText(-1);
                    return predicate(text) ? text : null;
                }
                catch (ElementNotAvailableException)
                {
                    return null;
                }
            },
            "The projected document did not reach the expected text.");
    }

    public static string WaitForSelectionText(
        TextPattern textPattern,
        Func<string, bool> predicate)
    {
        return WaitFor(
            () =>
            {
                try
                {
                    var selection = textPattern.GetSelection();
                    var text = selection.Length == 0
                        ? string.Empty
                        : selection[0].GetText(-1);
                    return predicate(text) ? text : null;
                }
                catch (ElementNotAvailableException)
                {
                    return null;
                }
            },
            "The projected selection did not reach the expected text.");
    }

    public AutomationElement[] WaitForWindows(Func<AutomationElement[], bool> predicate)
    {
        return WaitFor(
            () =>
            {
                var windows = FindWindows();
                return predicate(windows) ? windows : null;
            },
            "Azunote did not reach the expected number of windows.");
    }

    public static void OpenMenu(AutomationElement window, string name)
    {
        var menu = FindRequired(window, AutomationElement.NameProperty, name);
        if (menu.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out var expandCollapse)
            && expandCollapse is ExpandCollapsePattern pattern)
        {
            pattern.Expand();
            return;
        }

        Invoke(menu);
    }

    public static void InvokeMenuItem(
        AutomationElement window,
        string menuName,
        string itemName)
    {
        OpenMenu(window, menuName);
        var item = WaitFor(
            () => window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, itemName)),
            $"Could not find menu item {menuName} > {itemName}.");
        Invoke(item);
    }

    public void Dispose()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                if (!_process.WaitForExit(2000) && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _window = null;
        }
    }

    private void EnsureStarted()
    {
        if (_window is not null)
        {
            return;
        }

        RequireEnabled();
        if (!OperatingSystem.IsWindows())
        {
            throw new XunitException("Azunote UI tests require Windows.");
        }

        var executablePath = ResolveExecutablePath();
        var verificationDocument = ResolveVerificationDocument();
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        };
        startInfo.ArgumentList.Add(verificationDocument);

        _process = Process.Start(startInfo)
            ?? throw new XunitException($"Could not start Azunote: {executablePath}");

        _window = WaitFor(
            FindWindow,
            "Azunote did not expose a top-level UI Automation window.");
    }

    private AutomationElement? FindWindow()
    {
        if (_process is null || _process.HasExited)
        {
            return null;
        }

        try
        {
            _process.Refresh();
            if (_process.MainWindowHandle != IntPtr.Zero)
            {
                var mainWindow = AutomationElement.FromHandle(_process.MainWindowHandle);
                if (mainWindow is not null && FindEditor(mainWindow) is not null)
                {
                    return mainWindow;
                }
            }

            var windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                Condition.TrueCondition);
            foreach (AutomationElement window in windows)
            {
                try
                {
                    if (window.Current.ProcessId == _process.Id
                        && FindEditor(window) is not null)
                    {
                        return window;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // The window was recreated while the app was starting.
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            // The UI tree is still being created.
        }

        return null;
    }

    private AutomationElement[] FindWindows()
    {
        if (_process is null || _process.HasExited)
        {
            return [];
        }

        var windows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            Condition.TrueCondition);
        var result = new List<AutomationElement>();
        foreach (AutomationElement window in windows)
        {
            try
            {
                if (window.Current.ProcessId == _process.Id
                    && FindEditor(window) is not null)
                {
                    result.Add(window);
                }
            }
            catch (ElementNotAvailableException)
            {
                // The window was closed while the desktop tree was queried.
            }
        }

        return result.ToArray();
    }

    private static AutomationElement? FindEditor(AutomationElement window) =>
        window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.NameProperty,
                "Document editor"));

    private static AutomationElement FindRequired(
        AutomationElement root,
        AutomationProperty property,
        string value) =>
        root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(property, value))
        ?? throw new XunitException(
            $"Could not find UI Automation element {property.ProgrammaticName}={value}.");

    internal static AutomationElement WaitForElement(
        AutomationElement root,
        AutomationProperty property,
        string value) =>
        WaitFor(
            () => root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(property, value)),
            $"Could not find UI Automation element {property.ProgrammaticName}={value}.");

    internal static void WaitForElementHidden(
        AutomationElement root,
        AutomationProperty property,
        string value)
    {
        WaitFor(
            () =>
            {
                var element = root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(property, value));
                if (element is null)
                {
                    return root;
                }

                try
                {
                    return element.Current.IsOffscreen
                        ? root
                        : null;
                }
                catch (ElementNotAvailableException)
                {
                    return root;
                }
            },
            $"UI Automation element {property.ProgrammaticName}={value} remained visible.");
    }

    internal static void WaitForToggleState(
        AutomationElement element,
        ToggleState expected)
    {
        WaitFor(
            () =>
            {
                try
                {
                    var pattern = (TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern);
                    return pattern.Current.ToggleState == expected
                        ? element
                        : null;
                }
                catch (ElementNotAvailableException)
                {
                    return null;
                }
            },
            $"The toggle did not reach state {expected}.");
    }

    internal static void TryCloseFindReplace(AutomationElement window)
    {
        var close = window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Close"));
        if (close is not null
            && close.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)
            && invoke is InvokePattern invokePattern)
        {
            invokePattern.Invoke();
        }
    }

    private static void Invoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)
            && invoke is InvokePattern invokePattern)
        {
            invokePattern.Invoke();
            return;
        }

        if (element.TryGetCurrentPattern(
                SelectionItemPattern.Pattern,
                out var selection)
            && selection is SelectionItemPattern selectionPattern)
        {
            selectionPattern.Select();
            return;
        }

        throw new XunitException(
            $"UI Automation element {element.Current.Name} is not invokable.");
    }

    private static string ResolveExecutablePath()
    {
        var configured = Environment.GetEnvironmentVariable(ExecutableVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Path.GetFullPath(configured);
            if (File.Exists(path))
            {
                return path;
            }

            throw new XunitException($"AZUNOTE_EXE does not exist: {path}");
        }

        var repositoryRoot = FindRepositoryRoot();
        var defaultPath = Path.Combine(
            repositoryRoot,
            "src",
            "Azunote",
            "bin",
            "Debug",
            "net10.0-windows10.0.19041.0",
            "win-x64",
            "Azunote.exe");
        return File.Exists(defaultPath)
            ? defaultPath
            : throw new XunitException(
                $"Azunote.exe was not found at {defaultPath}. Build Azunote first or set AZUNOTE_EXE.");
    }

    private static string ResolveVerificationDocument()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Azunote.UiTests",
            "TestAssets",
            "verification.toml");
        return File.Exists(path)
            ? path
            : throw new XunitException($"Verification document was not found: {path}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Azunyan.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new XunitException(
            "Could not locate the repository root containing Azunyan.slnx.");
    }

    private static void RequireEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable(EnabledVariable);
        if (!string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new XunitException(
                $"Set {EnabledVariable}=1 to run Windows UI tests on an interactive desktop.");
        }
    }

    internal static T WaitFor<T>(Func<T?> action, string message)
        where T : class
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var result = action();
            if (result is not null)
            {
                return result;
            }

            Thread.Sleep(100);
        }

        throw new XunitException(message);
    }

}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class AzunoteUiFactAttribute : FactAttribute
{
    public AzunoteUiFactAttribute()
    {
        if (!AzunoteUiTestEnvironment.IsEnabled)
        {
            Skip = "Set AZUNOTE_UI_TESTS=1 on an unlocked Windows desktop to run this test.";
        }
    }
}

internal static class AzunoteUiTestEnvironment
{
    public static bool IsEnabled =>
        OperatingSystem.IsWindows()
        && (string.Equals(
                Environment.GetEnvironmentVariable("AZUNOTE_UI_TESTS"),
                "1",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                Environment.GetEnvironmentVariable("AZUNOTE_UI_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase));
}
