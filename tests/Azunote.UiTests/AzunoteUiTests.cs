using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Xunit;
using Xunit.Sdk;

namespace Azunote.UiTests;

/// <summary>
/// Windows UI Automation smoke tests for the unpackaged Azunote desktop app.
/// These tests are opt-in because they need an unlocked interactive desktop.
/// </summary>
public sealed class AzunoteUiTests : IDisposable
{
    private readonly AzunoteUiFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [AzunoteUiFact]
    public void Main_window_starts_without_initialization_exception()
    {
        Assert.NotNull(_fixture.Editor);
    }

    [AzunoteUiFact]
    public void Run_external_tool_dialog_opens_with_default_input_and_can_be_canceled()
    {
        var window = _fixture.Window;
        AzunoteUiFixture.InvokeMenuItem(window, "Tools", "Run External Tool...");

        var run = AzunoteUiFixture.WaitForElement(
            window,
            AutomationElement.NameProperty,
            "Run");
        Assert.Equal(ControlType.Button, run.Current.ControlType);

        var inputMode = AzunoteUiFixture.WaitFor(
            () =>
            {
                var comboBoxes = window.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ComboBox));
                foreach (AutomationElement comboBox in comboBoxes)
                {
                    if (comboBox.TryGetCurrentPattern(
                            SelectionPattern.Pattern,
                            out var pattern)
                        && pattern is SelectionPattern selection
                        && selection.Current.GetSelection() is [{ } selected]
                        && selected.Current.Name == "None")
                    {
                        return comboBox;
                    }
                }

                return null;
            },
            "The Input combo box did not start with None selected.");
        Assert.Equal(ControlType.ComboBox, inputMode.Current.ControlType);

        var cancel = AzunoteUiFixture.WaitForElement(
            window,
            AutomationElement.NameProperty,
            "Cancel");
        ((InvokePattern)cancel.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        AzunoteUiFixture.WaitForElementHidden(
            window,
            AutomationElement.AutomationIdProperty,
            "InputModeBox");
    }

    [AzunoteUiFact]
    public void Inline_prompt_accepts_multiple_messages_and_shows_each_tool_response()
    {
        var tools = Path.Combine(Path.GetDirectoryName(AzunoteUiFixture.ResolveExecutablePath())!, "appdata", "tools");
        Directory.CreateDirectory(tools);
        var suffix = Guid.NewGuid().ToString("N");
        var name = "Inline prompt Enter test " + suffix;
        var definition = Path.Combine(tools, name + ".tool.toml");
        File.WriteAllText(definition, $$"""
            name = "{{name}}"
            visibility = "always"
            [launch]
            cmd = "echo ResponseFromTool & ping 127.0.0.1 -n 9 > nul"
            input = "prompt:Type a message"
            stdout = "showPromptResponse"
            stream = "stdout"
            """);
        try
        {
            var window = _fixture.Window;
            AzunoteUiFixture.InvokeMenuItem(window, "Tools", name);

            var input = AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.AutomationIdProperty,
                "InlinePromptInput");
            var value = (ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern);
            const string firstMessage = "FirstSubmissionCheck";
            value.SetValue(firstMessage);
            input.SetFocus();
            SendEnterKey();

            Assert.True(input.Current.IsEnabled);
            var sendButton = AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.AutomationIdProperty,
                "InlinePromptSendButton");
            AzunoteUiFixture.WaitFor(
                () => sendButton.Current.Name == "Processing" ? sendButton : null,
                "The send button did not show its processing indicator.");
            const string secondMessage = "SecondSubmissionCheck";
            value.SetValue(secondMessage);
            Assert.Equal(secondMessage, value.Current.Value);

            AzunoteUiFixture.WaitFor(
                () => CountElementsWhoseNameContains(window, "ResponseFromTool") >= 1
                    ? window
                    : null,
            "The first inline-prompt response was not displayed.");
            Assert.Equal("Processing", sendButton.Current.Name);
            Assert.False(sendButton.Current.IsEnabled);

            AzunoteUiFixture.WaitFor(
                () => sendButton.Current.IsEnabled ? sendButton : null,
                "The send button did not re-enable after the first tool response.");
            Assert.Equal(secondMessage, value.Current.Value);
            input.SetFocus();
            SendEnterKey();
            AzunoteUiFixture.WaitFor(
                () => sendButton.Current.Name == "Processing" ? sendButton : null,
                "The send button did not show processing for the second message.");
            AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                secondMessage);
            AzunoteUiFixture.WaitFor(
                () => CountElementsWhoseNameContains(window, "ResponseFromTool") >= 2
                    ? window
                    : null,
                "The second inline-prompt response was not displayed.");

            var cancel = AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                "Cancel");
            ((InvokePattern)cancel.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            AzunoteUiFixture.WaitForElementHidden(
                window,
                AutomationElement.AutomationIdProperty,
                "InlinePromptInput");
        }
        finally
        {
            _fixture.Dispose();
            File.Delete(definition);
        }
    }

    [AzunoteUiFact]
    public void Tool_menu_does_not_refresh_when_selection_changes_while_open()
    {
        // A unique definition in the test executable's portable data directory;
        // never edit the user's global settings or an existing tool definition.
        var tools = Path.Combine(Path.GetDirectoryName(AzunoteUiFixture.ResolveExecutablePath())!, "appdata", "tools");
        Directory.CreateDirectory(tools);
        var name = "Selection test " + Guid.NewGuid().ToString("N");
        var definition = Path.Combine(tools, name + ".tool.toml");
        File.WriteAllText(definition, $"""
            name = "{name}"
            visibility = "always"
            [when]
            selection = "nonEmpty"
            [launch]
            cmd = "echo unused"
            """);
        try
        {
            var window = _fixture.Window;
            var text = AzunoteUiFixture.WaitForTextPattern(_fixture.Editor);
            AzunoteUiFixture.WaitForDocumentText(text, value => value.Contains("日本語", StringComparison.Ordinal));
            AzunoteUiFixture.OpenMenu(window, "Tools");
            var item = AzunoteUiFixture.WaitForElement(window, AutomationElement.NameProperty, name);
            Assert.False(item.Current.IsEnabled);

            var selection = text.DocumentRange.FindText("日本語", false, false)!;
            selection.Select();
            Assert.Equal("日本語", AzunoteUiFixture.WaitForSelectionText(text, value => value == "日本語"));
            Assert.False(item.Current.IsEnabled);
        }
        finally
        {
            _fixture.Dispose();
            File.Delete(definition);
        }
    }

    [AzunoteUiFact]
    public void Open_tool_menu_tracks_dotenv_changes_without_editor_input()
    {
        var root = Directory.CreateTempSubdirectory("azunote-watched-menu-");
        var document = Path.Combine(root.FullName, "test.txt");
        File.WriteAllText(document, "日本語の環境変更テスト");
        var tools = Path.Combine(Path.GetDirectoryName(AzunoteUiFixture.ResolveExecutablePath())!, "appdata", "tools");
        Directory.CreateDirectory(tools);
        var suffix = Guid.NewGuid().ToString("N");
        var name = "Watched cache test " + suffix;
        var variable = "AZUNOTE_WATCH_TEST_" + suffix;
        var definition = Path.Combine(tools, name + ".tool.toml");
        File.WriteAllText(definition, $$"""
            name = "{{name}}"
            visibility = "always"
            [launch]
            command = "${env:{{variable}}}"
            """);
        using var fixture = new AzunoteUiFixture(document);
        try
        {
            var window = fixture.Window;
            AzunoteUiFixture.WaitForDocumentText(AzunoteUiFixture.WaitForTextPattern(fixture.Editor),
                value => value == "日本語の環境変更テスト");
            AzunoteUiFixture.OpenMenu(window, "Tools");
            var item = AzunoteUiFixture.WaitForElement(window, AutomationElement.NameProperty, name);
            Assert.False(item.Current.IsEnabled);
            var identity = item.GetRuntimeId();
            var dotenv = Path.Combine(root.FullName, ".env");
            File.WriteAllText(dotenv, variable + "=" + Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            AzunoteUiFixture.WaitFor(() => item.Current.IsEnabled ? item : null,
                "Creating .env did not enable the open menu item without input.");
            File.WriteAllText(dotenv, variable + "=" + Path.Combine(root.FullName, "missing.exe"));
            AzunoteUiFixture.WaitFor(() => !item.Current.IsEnabled ? item : null,
                "Changing .env did not disable the open menu item without input.");
            File.WriteAllText(dotenv, variable + "=" + Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            AzunoteUiFixture.WaitFor(() => item.Current.IsEnabled ? item : null,
                "Restoring .env did not enable the open menu item.");
            File.Delete(dotenv);
            AzunoteUiFixture.WaitFor(() => !item.Current.IsEnabled ? item : null,
                "Deleting .env did not disable the open menu item.");
            Assert.Equal(identity, item.GetRuntimeId());
        }
        finally
        {
            fixture.Dispose();
            File.Delete(definition);
            root.Delete(recursive: true);
        }
    }

    [AzunoteUiFact]
    public void Help_opens_read_only_documents_with_selectable_text_and_readonly_titles()
    {
        var source = _fixture.Window;
        foreach (var (menu, fileName, expectedText) in new[]
        {
            ("Third Party Notices", "THIRD-PARTY-NOTICES.md", "Third-party notices"),
            ("View License", "LICENSE", "Permission is granted")
        })
        {
            AzunoteUiFixture.InvokeMenuItem(source, "Help", menu);
            // Development builds append " (Dev)" to the application name.
            var title = $"{fileName} [READONLY] - Azunote";
            bool IsLegalWindow(AutomationElement window) =>
                window.Current.Name.StartsWith(title, StringComparison.Ordinal);
            var legalWindow = _fixture.WaitForWindows(windows => windows.Any(IsLegalWindow))
                .First(IsLegalWindow);
            using var process = Process.GetProcessById(legalWindow.Current.ProcessId);
            try
            {
                var editor = AzunoteUiFixture.WaitForElement(
                    legalWindow, AutomationElement.NameProperty, "Document editor");
                var value = AzunoteUiFixture.WaitForValuePattern(editor,
                    text => text.Contains(expectedText, StringComparison.Ordinal));
                Assert.True(value.Current.IsReadOnly);
                var original = value.Current.Value;

                var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
                Assert.Single(textPattern.GetSelection());
                editor.SetFocus();
                var range = textPattern.DocumentRange.FindText(expectedText, false, false);
                Assert.NotNull(range);
                range!.Select();
                Assert.Equal(expectedText, AzunoteUiFixture.WaitForSelectionText(
                    textPattern, text => text == expectedText));

                Assert.Throws<InvalidOperationException>(() => value.SetValue("overwrite"));
                Assert.Equal(original, value.Current.Value);

                AzunoteUiFixture.OpenMenu(legalWindow, "File");
                Assert.False(AzunoteUiFixture.WaitForElement(
                    legalWindow, AutomationElement.NameProperty, "Save").Current.IsEnabled);
                Assert.False(AzunoteUiFixture.WaitForElement(
                    legalWindow, AutomationElement.NameProperty, "Save As...").Current.IsEnabled);
                AzunoteUiFixture.CloseMenu(legalWindow, "File");
                ((WindowPattern)legalWindow.GetCurrentPattern(WindowPattern.Pattern)).Close();
            }
            catch (Exception exception)
            {
                throw new XunitException($"{fileName}: process exited={process.HasExited}; {exception}");
            }
        }
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
    public void Line_shortcuts_move_duplicate_and_delete_the_caret_line()
    {
        const ushort Shift = 0x10, Control = 0x11, Alt = 0x12, Up = 0x26, Down = 0x28, K = 0x4B;
        var editor = _fixture.Editor;
        var editorValue = AzunoteUiFixture.WaitForValuePattern(editor);
        var original = editorValue.Current.Value;

        try
        {
            editorValue.SetValue("first\nsecond\nthird");
            var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "first\nsecond\nthird");
            textPattern.DocumentRange.FindText("first", false, false)!.Select();
            editor.SetFocus();

            SendKeyChord(Alt, Down);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "second\nfirst\nthird");

            SendKeyChord(Shift, Alt, Up);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "second\nfirst\nfirst\nthird");

            SendKeyChord(Control, Shift, K);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "second\nfirst\nthird");

            AzunoteUiFixture.OpenMenu(_fixture.Window, "Edit");
            InvokeSubmenuItem(_fixture.Window, "Line", "Move Line Up");
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "first\nsecond\nthird");
        }
        finally
        {
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
        }
    }

    [AzunoteUiFact]
    public void Add_next_occurrence_edits_every_match_and_toggle_comment_uses_the_language()
    {
        const ushort Control = 0x11, D = 0x44, X = 0x58, Slash = 0xBF;
        var root = Directory.CreateTempSubdirectory("azunote-occurrence-");
        var document = Path.Combine(root.FullName, "test.cs");
        File.WriteAllText(document, "foo bar foo");
        using var fixture = new AzunoteUiFixture(document);
        try
        {
            var editor = fixture.Editor;
            var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "foo bar foo");
            var caret = textPattern.DocumentRange.FindText("foo", false, false)!;
            caret.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            caret.Select();
            editor.SetFocus();

            SendKeyChord(Control, D);
            SendKeyChord(Control, D);
            SendKeyChord(X);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "x bar x");

            SendKeyChord(Control, Slash);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "// x bar x");
            SendKeyChord(Control, Slash);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "x bar x");
        }
        finally
        {
            fixture.Dispose();
            root.Delete(recursive: true);
        }
    }

    [AzunoteUiFact]
    public void External_tool_shortcut_takes_precedence_over_line_shortcut_only_when_enabled()
    {
        const ushort Alt = 0x12, Down = 0x28;
        var tools = Path.Combine(Path.GetDirectoryName(AzunoteUiFixture.ResolveExecutablePath())!, "appdata", "tools");
        Directory.CreateDirectory(tools);
        var name = "Shortcut precedence test " + Guid.NewGuid().ToString("N");
        var definition = Path.Combine(tools, name + ".tool.toml");
        File.WriteAllText(definition, $"""
            name = "{name}"
            shortcut = "Alt+Down"
            visibility = "always"
            [when]
            selection = "nonEmpty"
            [launch]
            cmd = "echo TOOL"
            output = "replaceSelection"
            """);
        try
        {
            // Wait until the settings watcher has loaded the definition.
            AzunoteUiFixture.OpenMenu(_fixture.Window, "Tools");
            AzunoteUiFixture.WaitForElement(_fixture.Window, AutomationElement.NameProperty, name);
            AzunoteUiFixture.CloseMenu(_fixture.Window, "Tools");

            var editor = _fixture.Editor;
            var editorValue = AzunoteUiFixture.WaitForValuePattern(editor);
            editorValue.SetValue("first\nsecond\nthird");
            var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "first\nsecond\nthird");

            // With a selection, the enabled tool owns the shortcut.
            textPattern.DocumentRange.FindText("first", false, false)!.Select();
            editor.SetFocus();
            SendKeyChord(Alt, Down);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text.StartsWith("TOOL", StringComparison.Ordinal));
            // Neither a second tool run nor the line command may follow.
            Thread.Sleep(1000);
            var afterTool = AzunoteUiFixture.WaitForDocumentText(textPattern, _ => true);
            Assert.Single(afterTool.Split("TOOL"), part => part.EndsWith("\nsecond\nthird", StringComparison.Ordinal));
            Assert.Equal(2, afterTool.Split("TOOL").Length);

            // Without a selection, the tool is unavailable and the editor moves the line.
            editorValue.SetValue("first\nsecond\nthird");
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "first\nsecond\nthird");
            var caret = textPattern.DocumentRange.FindText("first", false, false)!;
            caret.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            caret.Select();
            editor.SetFocus();
            SendKeyChord(Alt, Down);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "second\nfirst\nthird");
        }
        finally
        {
            _fixture.Dispose();
            File.Delete(definition);
        }
    }

    [AzunoteUiFact]
    public void Replace_all_in_selection_is_undone_in_one_step()
    {
        var window = _fixture.Window;
        var editor = _fixture.Editor;
        var editorValue = AzunoteUiFixture.WaitForValuePattern(editor);
        var original = editorValue.Current.Value;

        try
        {
            editorValue.SetValue("one one one");
            var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "one one one");
            textPattern.DocumentRange.FindText("one one", false, false)!.Select();

            AzunoteUiFixture.InvokeMenuItem(window, "Edit", "Replace...");
            var inSelection = AzunoteUiFixture.WaitForElement(
                window,
                AutomationElement.NameProperty,
                "Find in Selection");
            ((TogglePattern)inSelection.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
            AzunoteUiFixture.WaitForToggleState(inSelection, ToggleState.On);
            SetElementValue(window, "Find text", "one");
            SetElementValue(window, "Replace text", "1");
            ((InvokePattern)AzunoteUiFixture.WaitForElement(
                    window,
                    AutomationElement.NameProperty,
                    "Replace all")
                .GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "1 1 one");

            AzunoteUiFixture.InvokeMenuItem(window, "Edit", "Undo");
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "one one one");
        }
        finally
        {
            AzunoteUiFixture.TryCloseFindReplace(window);
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
        }
    }

    [AzunoteUiFact]
    public void Command_palette_runs_menu_commands_and_switches_to_recent_files()
    {
        const ushort Shift = 0x10, Control = 0x11, P = 0x50, F1 = 0x70, Escape = 0x1B;
        var window = _fixture.Window;
        var editor = _fixture.Editor;
        var editorValue = AzunoteUiFixture.WaitForValuePattern(editor);
        var original = editorValue.Current.Value;

        try
        {
            editorValue.SetValue("first\nsecond");
            var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "first\nsecond");
            textPattern.DocumentRange.FindText("second", false, false)!.Select();
            editor.SetFocus();

            SendKeyChord(Control, Shift, P);
            SetCommandPaletteText(WaitForCommandPalette(window, text => text == ">"), ">mlu");
            AzunoteUiFixture.WaitForElement(window, AutomationElement.NameProperty, "Move Line Up");
            SendEnterKey();
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "second\nfirst");

            SendKeyChord(F1);
            SetCommandPaletteText(WaitForCommandPalette(window, text => text == ">"), ">open recent");
            AzunoteUiFixture.WaitForElement(window, AutomationElement.NameProperty, "Open Recent");
            SendEnterKey();
            WaitForCommandPalette(window, text => text.Length == 0);

            SendKeyChord(Escape);
            AzunoteUiFixture.WaitFor(
                () => FindCommandPalette(window) is null ? window : null,
                "Escape did not close the command palette.");
        }
        finally
        {
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
        }
    }

    [AzunoteUiFact]
    public void Command_palette_without_prefix_switches_to_another_open_window()
    {
        const ushort F1 = 0x70;
        var original = _fixture.Window;
        var originalHandle = original.Current.NativeWindowHandle;
        AzunoteUiFixture.InvokeMenuItem(original, "File", "New");
        var created = _fixture.WaitForWindows(windows => windows.Length >= 2)
            .First(window => window.Current.NativeWindowHandle != originalHandle);
        AzunoteUiFixture.WaitForElement(created, AutomationElement.NameProperty, "Document editor").SetFocus();

        SendKeyChord(F1);
        SetCommandPaletteText(WaitForCommandPalette(created, text => text == ">"), string.Empty);
        AzunoteUiFixture.WaitFor(
            () => created.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)),
            "The command palette did not list the other window.");
        SendEnterKey();

        AzunoteUiFixture.WaitFor(
            () => GetForegroundWindow() == originalHandle ? original : null,
            "Choosing the other window did not activate it.");
    }

    [AzunoteUiFact]
    public void F5_inserts_the_date_and_time_and_typing_continues_after_it()
    {
        const ushort F5 = 0x74, X = 0x58;
        var editor = _fixture.Editor;
        var editorValue = AzunoteUiFixture.WaitForValuePattern(editor);
        var original = editorValue.Current.Value;

        try
        {
            editorValue.SetValue("at ");
            var textPattern = AzunoteUiFixture.WaitForTextPattern(editor);
            AzunoteUiFixture.WaitForDocumentText(textPattern, text => text == "at ");
            textPattern.DocumentRange.FindText("at ", false, false)!.Select();
            editor.SetFocus();
            SendKeyChord(0x23); // End

            SendKeyChord(F5);
            AzunoteUiFixture.WaitForDocumentText(
                textPattern,
                text => Regex.IsMatch(text, @"^at \d{4}-\d{2}-\d{2} \d{2}:\d{2}$"));
            SendKeyChord(X);
            AzunoteUiFixture.WaitForDocumentText(
                textPattern,
                text => Regex.IsMatch(text, @"^at \d{4}-\d{2}-\d{2} \d{2}:\d{2}x$", RegexOptions.IgnoreCase));
        }
        finally
        {
            AzunoteUiFixture.WaitForValuePattern(editor).SetValue(original);
        }
    }

    private static AutomationElement? FindCommandPalette(AutomationElement window) =>
        window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Command palette"));

    private static AutomationElement WaitForCommandPalette(
        AutomationElement window,
        Func<string, bool> predicate) =>
        AzunoteUiFixture.WaitFor(
            () => FindCommandPalette(window) is { } palette
                && palette.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
                && predicate(((ValuePattern)value).Current.Value)
                    ? palette
                    : null,
            "The command palette did not show the expected text.");

    private static void SetCommandPaletteText(AutomationElement palette, string text) =>
        ((ValuePattern)palette.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);

    [DllImport("user32.dll")]
    private static extern int GetForegroundWindow();

    private static void SetElementValue(AutomationElement window, string name, string value) =>
        ((ValuePattern)AzunoteUiFixture.WaitForElement(window, AutomationElement.NameProperty, name)
            .GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);

    [AzunoteUiFact]
    public void Closing_the_last_window_keeps_unsaved_text_without_asking()
    {
        var root = Directory.CreateTempSubdirectory("azunote-hot-exit-");
        var document = Path.Combine(root.FullName, "note.txt");
        File.WriteAllText(document, "saved");
        using var fixture = new AzunoteUiFixture(document);
        try
        {
            var editor = fixture.Editor;
            AzunoteUiFixture.WaitForValuePattern(editor, text => text == "saved").SetValue("unsaved");
            AzunoteUiFixture.WaitForDocumentText(AzunoteUiFixture.WaitForTextPattern(editor), text => text == "unsaved");

            var window = fixture.Window;
            AzunoteUiFixture.WaitFor(
                () => window.Current.Name.StartsWith("note.txt *", StringComparison.Ordinal) ? window : null,
                "The edited document did not become dirty.");
            ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();

            Assert.True(fixture.WaitForExit(10000), "Closing the last dirty window did not exit without a prompt.");
            var appData = Path.GetDirectoryName(AzunoteUiFixture.BackupDirectory)!;
            var backups = Directory.Exists(AzunoteUiFixture.BackupDirectory)
                ? Directory.GetFiles(AzunoteUiFixture.BackupDirectory, "*.backup")
                : [];
            Assert.True(
                backups.Length == 1,
                "Backups: " + string.Join(", ", Directory.GetFileSystemEntries(appData, "*", SearchOption.AllDirectories)));
            var backup = File.ReadAllText(backups[0]);
            Assert.Equal($"Azunote backup 1\n{document}\n\nunsaved", backup);
            Assert.Equal("saved", File.ReadAllText(document));
        }
        finally
        {
            fixture.Dispose();
            root.Delete(recursive: true);
        }
    }

    [AzunoteUiFact]
    public void Exit_closes_every_window_and_keeps_unsaved_text()
    {
        var window = _fixture.Window;
        var editor = _fixture.Editor;
        AzunoteUiFixture.WaitForValuePattern(editor).SetValue("exit draft");
        AzunoteUiFixture.WaitFor(
            () => window.Current.Name.Contains(" *", StringComparison.Ordinal) ? window : null,
            "The edited document did not become dirty.");
        AzunoteUiFixture.InvokeMenuItem(window, "File", "New");
        _fixture.WaitForWindows(windows => windows.Length == 2);

        AzunoteUiFixture.InvokeMenuItem(window, "File", "Exit");

        Assert.True(_fixture.WaitForExit(10000), "File > Exit did not close every window.");
        var backup = File.ReadAllText(Assert.Single(Directory.GetFiles(AzunoteUiFixture.BackupDirectory, "*.backup")));
        Assert.EndsWith("\n\nexit draft", backup);
    }

    [AzunoteUiFact]
    public void Launching_with_a_backed_up_file_shows_its_restored_window_only()
    {
        var root = Directory.CreateTempSubdirectory("azunote-restore-same-file-");
        var document = Path.Combine(root.FullName, "note.txt");
        File.WriteAllText(document, "saved");
        Directory.CreateDirectory(AzunoteUiFixture.BackupDirectory);
        File.WriteAllText(
            Path.Combine(AzunoteUiFixture.BackupDirectory, Guid.NewGuid().ToString("N") + ".backup"),
            $"Azunote backup 1\n{document}\n\nunsaved");
        using var fixture = new AzunoteUiFixture(document, keepBackups: true);
        try
        {
            _ = fixture.Window;
            var window = fixture.WaitForWindows(windows =>
                    windows.Length == 1
                    && windows[0].Current.Name.StartsWith("note.txt *", StringComparison.Ordinal))
                .Single();
            var editor = AzunoteUiFixture.WaitForElement(window, AutomationElement.NameProperty, "Document editor");
            AzunoteUiFixture.WaitForDocumentText(AzunoteUiFixture.WaitForTextPattern(editor), text => text == "unsaved");

            ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
            Assert.True(fixture.WaitForExit(10000), "Another window kept Azunote running.");
        }
        finally
        {
            fixture.Dispose();
            Directory.Delete(AzunoteUiFixture.BackupDirectory, recursive: true);
            root.Delete(recursive: true);
        }
    }

    [AzunoteUiFact]
    public void Menus_change_the_saved_encoding_and_line_ending()
    {
        var root = Directory.CreateTempSubdirectory("azunote-format-");
        var document = Path.Combine(root.FullName, "format.txt");
        File.WriteAllText(document, "a\r\nb");
        using var fixture = new AzunoteUiFixture(document);
        try
        {
            var window = fixture.Window;
            AzunoteUiFixture.WaitForDocumentText(AzunoteUiFixture.WaitForTextPattern(fixture.Editor), text => text == "a\r\nb");

            AzunoteUiFixture.OpenMenu(window, "View");
            InvokeSubmenuItem(window, "Line Ending", "LF");
            AzunoteUiFixture.WaitFor(
                () => window.Current.Name.StartsWith("format.txt *", StringComparison.Ordinal) ? window : null,
                "Choosing LF did not leave the document unsaved.");

            AzunoteUiFixture.OpenMenu(window, "File");
            InvokeSubmenuItem(window, "Save with Encoding", "UTF-16 LE");
            AzunoteUiFixture.WaitFor(
                () => File.ReadAllBytes(document) is [0xFF, 0xFE, ..] ? document : null,
                "Save with Encoding did not write UTF-16 LE.");
            Assert.Equal("a\nb", File.ReadAllText(document));
        }
        finally
        {
            fixture.Dispose();
            root.Delete(recursive: true);
        }
    }

    [AzunoteUiFact]
    public void Save_as_dialog_places_the_encoding_beside_the_save_button()
    {
        const ushort Control = 0x11, Shift = 0x10, S = 0x53, Escape = 0x1B;
        var window = _fixture.Window;
        _fixture.Editor.SetFocus();
        SendKeyChord(Control, Shift, S);

        // The dialog follows the system language, so its Save button is found
        // by its IDOK automation id, and a combo box is named by its selection.
        var encoding = AzunoteUiFixture.WaitFor(
            () => FindInProcess(window, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
                new OrCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "UTF-8"),
                    new PropertyCondition(AutomationElement.NameProperty, "Encoding:")))),
            "The Save As dialog did not show the encoding combo box.");
        var save = AzunoteUiFixture.WaitFor(
            () => FindInProcess(window, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.AutomationIdProperty, "1"))),
            "The Save As dialog did not show its Save button.");
        var label = AzunoteUiFixture.WaitFor(
            () => FindInProcess(window, new PropertyCondition(AutomationElement.NameProperty, "Encoding:")),
            "The Save As dialog did not label the encoding.");
        try
        {
            var saveBounds = save.Current.BoundingRectangle;
            foreach (var element in new[] { encoding, label })
            {
                var bounds = element.Current.BoundingRectangle;
                Assert.True(
                    Math.Abs((bounds.Top + bounds.Bottom) / 2 - (saveBounds.Top + saveBounds.Bottom) / 2) < saveBounds.Height,
                    $"{element.Current.ControlType.ProgrammaticName} {bounds} is not on the Save button's row {saveBounds}.");
            }

            foreach (var lineEnding in new[] { "Windows (CRLF)", "Unix (LF)", "Classic Mac (CR)" })
            {
                Assert.Null(FindInProcess(window, new PropertyCondition(AutomationElement.NameProperty, lineEnding)));
            }

            ((ExpandCollapsePattern)encoding.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            AzunoteUiFixture.WaitFor(
                () => encoding.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Latin-1")),
                "The encoding combo box does not offer Latin-1.");
            ((ExpandCollapsePattern)encoding.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        }
        finally
        {
            SendKeyChord(Escape);
            AzunoteUiFixture.WaitFor(
                () => FindInProcess(window, new PropertyCondition(AutomationElement.AutomationIdProperty, "1")) is null ? window : null,
                "The Save As dialog did not close.");
        }

        static AutomationElement? FindInProcess(AutomationElement window, Condition condition)
        {
            foreach (AutomationElement candidate in AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, window.Current.ProcessId)))
            {
                if (candidate.FindFirst(TreeScope.Descendants, condition) is { } element)
                {
                    return element;
                }
            }

            return null;
        }
    }

    [AzunoteUiFact]
    public void Title_marks_a_document_whose_file_was_deleted()
    {
        var root = Directory.CreateTempSubdirectory("azunote-deleted-");
        var document = Path.Combine(root.FullName, "gone.txt");
        File.WriteAllText(document, "text");
        using var fixture = new AzunoteUiFixture(document);
        try
        {
            var window = fixture.Window;
            AzunoteUiFixture.WaitForDocumentText(AzunoteUiFixture.WaitForTextPattern(fixture.Editor), text => text == "text");

            File.Delete(document);
            AzunoteUiFixture.WaitFor(
                () => window.Current.Name.StartsWith("gone.txt * [DELETED]", StringComparison.Ordinal) ? window : null,
                $"The title did not mark the deleted file: {window.Current.Name}");
        }
        finally
        {
            fixture.Dispose();
            root.Delete(recursive: true);
        }
    }

    [AzunoteUiFact]
    public void Launch_restores_backed_up_documents_in_their_own_windows()
    {
        Directory.CreateDirectory(AzunoteUiFixture.BackupDirectory);
        File.WriteAllText(
            Path.Combine(AzunoteUiFixture.BackupDirectory, Guid.NewGuid().ToString("N") + ".backup"),
            "Azunote backup 1\n\n\nrestored draft");
        using var fixture = new AzunoteUiFixture(keepBackups: true);
        try
        {
            _ = fixture.Window;
            var restored = fixture.WaitForWindows(windows => windows.Any(IsRestoredWindow)).First(IsRestoredWindow);
            var editor = AzunoteUiFixture.WaitForElement(restored, AutomationElement.NameProperty, "Document editor");
            AzunoteUiFixture.WaitForDocumentText(
                AzunoteUiFixture.WaitForTextPattern(editor),
                text => text == "restored draft");
        }
        finally
        {
            fixture.Dispose();
            Directory.Delete(AzunoteUiFixture.BackupDirectory, recursive: true);
        }

        static bool IsRestoredWindow(AutomationElement window) =>
            window.Current.Name.StartsWith("Untitled *", StringComparison.Ordinal);
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
    public void Duplicate_window_exposes_selectable_text()
    {
        var source = _fixture.Window;
        var before = _fixture.WaitForWindows(_ => true).Select(w => w.Current.NativeWindowHandle).ToHashSet();
        AzunoteUiFixture.InvokeMenuItem(source, "Window", "Duplicate Window");
        var duplicate = _fixture.WaitForWindows(windows => windows.Any(w => !before.Contains(w.Current.NativeWindowHandle)))
            .First(w => !before.Contains(w.Current.NativeWindowHandle));
        var editor = AzunoteUiFixture.WaitForElement(duplicate, AutomationElement.NameProperty, "Document editor");
        var text = AzunoteUiFixture.WaitForTextPattern(editor);
        var range = text.DocumentRange.FindText("日本語", false, false);
        Assert.NotNull(range);
        range!.Select();
        Assert.Equal("日本語", AzunoteUiFixture.WaitForSelectionText(text, selected => selected == "日本語"));
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
                            TogglePattern.Pattern,
                            out var pattern)
                        && pattern is TogglePattern toggle
                        && toggle.Current.ToggleState == ToggleState.On);
                return selectedItems == 1 ? window : null;
            },
            "The active window did not have exactly one checked document in the Window menu.");
    }

    private static void SendEnterKey() => SendKeyChord(0x0D);

    /// <summary>
    /// Presses the keys in order and releases them in reverse order.
    /// </summary>
    private static void SendKeyChord(params ushort[] keys)
    {
        static NativeInput Key(ushort key, bool up) => new()
        {
            Type = 1,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    // Arrow keys are extended keys; KEYEVENTF_KEYUP is 0x0002.
                    Flags = (key is >= 0x25 and <= 0x28 ? 0x0001u : 0u) | (up ? 0x0002u : 0u)
                }
            }
        };

        var input = keys.Select(key => Key(key, up: false))
            .Concat(keys.Reverse().Select(key => Key(key, up: true)))
            .ToArray();
        Assert.Equal((uint)input.Length, SendInput((uint)input.Length, input, Marshal.SizeOf<NativeInput>()));
    }

    private static int CountElementsWhoseNameContains(AutomationElement root, string value)
    {
        var elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        var count = 0;
        foreach (AutomationElement element in elements)
        {
            try
            {
                if (element.Current.Name.Contains(value, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            catch (ElementNotAvailableException)
            {
                // A transcript element can be replaced while the chat updates.
            }
        }

        return count;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, NativeInput[] inputs, int size);

    /// <summary>Expands a submenu of an open flyout and invokes one of its items.</summary>
    private static void InvokeSubmenuItem(AutomationElement window, string submenuName, string itemName)
    {
        var submenu = AzunoteUiFixture.WaitFor(
            () => FindMenuItem(window, submenuName),
            $"Could not find the {submenuName} submenu.");
        ((ExpandCollapsePattern)submenu.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        InvokeFlyoutItem(window, itemName);
    }

    /// <summary>Invokes an item of an open flyout.</summary>
    private static void InvokeFlyoutItem(AutomationElement window, string itemName)
    {
        var item = AzunoteUiFixture.WaitFor(
            () => FindMenuItem(window, itemName),
            $"Could not find the {itemName} menu item.");
        if (item.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
        }
        else
        {
            ((TogglePattern)item.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        }
    }

    /// <summary>
    /// A submenu can be hosted in its own popup window, so every top-level
    /// window of the process is searched for a visible item.
    /// </summary>
    private static AutomationElement? FindMenuItem(AutomationElement window, string name)
    {
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
        var processWindows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, window.Current.ProcessId));
        foreach (AutomationElement candidate in processWindows)
        {
            foreach (AutomationElement item in candidate.FindAll(TreeScope.Descendants, condition))
            {
                if (!item.Current.IsOffscreen)
                {
                    return item;
                }
            }
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}

public sealed class AzunoteUiFixture : IDisposable
{
    private readonly string? _verificationDocument;
    private readonly bool _keepBackups;

    /// <summary>
    /// Unsaved documents left by an earlier test would be restored as extra
    /// windows, so backups are removed before each launch unless
    /// <paramref name="keepBackups"/> asks to restore them.
    /// </summary>
    public AzunoteUiFixture(string? verificationDocument = null, bool keepBackups = false)
    {
        _verificationDocument = verificationDocument;
        _keepBackups = keepBackups;
    }

    public static string BackupDirectory =>
        Path.Combine(Path.GetDirectoryName(ResolveExecutablePath())!, "appdata", "backups");

    public bool WaitForExit(int milliseconds) => _process?.WaitForExit(milliseconds) ?? true;
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
            if (pattern.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
            {
                pattern.Expand();
            }
            return;
        }

        Invoke(menu);
    }

    public static void CloseMenu(AutomationElement window, string name)
    {
        var menu = FindRequired(window, AutomationElement.NameProperty, name);
        var pattern = (ExpandCollapsePattern)menu.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        if (pattern.Current.ExpandCollapseState != ExpandCollapseState.Collapsed)
        {
            pattern.Collapse();
        }
    }

    public static void InvokeMenuItem(
        AutomationElement window,
        string menuName,
        string itemName)
    {
        OpenMenu(window, menuName);
        var item = WaitFor(
            () =>
            {
                var candidate = window.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, itemName));
                if (candidate is not null && !candidate.Current.IsOffscreen)
                {
                    return candidate;
                }
                // A preceding flyout can still be closing when the first
                // expand request is sent. Retry against its current state.
                OpenMenu(window, menuName);
                return null;
            },
            $"Could not find menu item {menuName} > {itemName}.");
        Invoke(item);
        WaitFor(
            () =>
            {
                try
                {
                    return item.Current.IsOffscreen ? item : null;
                }
                catch (ElementNotAvailableException)
                {
                    return item;
                }
            },
            $"Menu item {menuName} > {itemName} did not close after invocation.");
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
        // An appdata directory beside the executable makes Azunote keep its
        // settings and backups there, where the tests can clean them up.
        Directory.CreateDirectory(Path.GetDirectoryName(BackupDirectory)!);
        if (!_keepBackups && Directory.Exists(BackupDirectory))
        {
            Directory.Delete(BackupDirectory, recursive: true);
        }

        var verificationDocument = _verificationDocument ?? ResolveVerificationDocument();
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

    internal static string ResolveExecutablePath()
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
            // The tests are built for the same runtime identifier as the app.
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64",
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
