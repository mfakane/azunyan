using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class LanguageModeControllerTests
{
    [Fact]
    public void Opened_path_selects_mode_and_applies_azunote_providers()
    {
        var editor = new FakeEditorView();
        var menu = new FakeLanguageModeMenuView();
        var controller = new LanguageModeController(
            editor,
            menu,
            () => "settings.toml");

        controller.Initialize();
        controller.DocumentOpened("settings.toml");

        Assert.Equal("azunote", controller.CurrentModeId);
        Assert.NotNull(editor.LanguageConfiguration?.Syntax);
        Assert.NotNull(editor.LanguageConfiguration?.Completion);
        Assert.IsType<AzunoteFoldingProvider>(editor.LanguageConfiguration?.Folding);
        Assert.Equal("azunote", menu.SelectedId);
    }

    [Fact]
    public void Selected_mode_reports_the_extensions_an_untitled_document_matches()
    {
        // An untitled document has no extension of its own, so this is what
        // a tool's `[when].extensions` is matched against.
        var editor = new FakeEditorView();
        var menu = new FakeLanguageModeMenuView();
        var controller = new LanguageModeController(
            editor,
            menu,
            () => null);

        controller.Initialize();
        menu.SelectFromMenu("json");

        Assert.Equal("json", controller.CurrentModeId);
        Assert.Contains(".json", controller.CurrentModeFileExtensions);
        Assert.Contains(".jsonc", controller.CurrentModeFileExtensions);
        Assert.Contains(".json5", controller.CurrentModeFileExtensions);
    }

    [Fact]
    public void Manual_selection_prevents_later_path_auto_selection()
    {
        var editor = new FakeEditorView();
        var menu = new FakeLanguageModeMenuView();
        var controller = new LanguageModeController(
            editor,
            menu,
            () => "settings.toml");

        controller.Initialize();
        menu.SelectFromMenu("plain-text");
        controller.SelectForPath("settings.toml");

        Assert.Equal("plain-text", controller.CurrentModeId);
        Assert.True(controller.IsManuallySelected);
    }

    [Fact]
    public void Uses_document_word_completion_for_plain_text_and_other_language_modes()
    {
        var editor = new FakeEditorView();
        var menu = new FakeLanguageModeMenuView();
        var controller = new LanguageModeController(
            editor,
            menu,
            () => null);

        controller.Initialize();
        Assert.IsType<DocumentWordCompletionProvider>(editor.LanguageConfiguration?.Completion);

        controller.DocumentOpened("config.yml");
        Assert.Equal("yaml", controller.CurrentModeId);
        Assert.IsType<DocumentWordCompletionProvider>(editor.LanguageConfiguration?.Completion);
    }

    [Theory]
    [InlineData("data.json")]
    [InlineData("config.jsonc")]
    [InlineData("settings.json5")]
    [InlineData("config.yml")]
    [InlineData("settings.toml")]
    public void Structured_document_modes_supply_a_folding_provider(string path)
    {
        var editor = new FakeEditorView();
        var menu = new FakeLanguageModeMenuView();
        var controller = new LanguageModeController(editor, menu, () => path);

        controller.Initialize();
        controller.DocumentOpened(path);

        Assert.NotNull(editor.LanguageConfiguration?.Folding);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("settings.toml")]
    [InlineData("program.cs")]
    public async Task Every_language_mode_highlights_urls(string path)
    {
        var editor = new FakeEditorView();
        var menu = new FakeLanguageModeMenuView();
        var controller = new LanguageModeController(editor, menu, () => path);

        controller.Initialize();
        controller.DocumentOpened(path);

        const string text = "x https://example.com/page";
        var syntax = editor.LanguageConfiguration?.Syntax;
        Assert.NotNull(syntax);
        var spans = await syntax!.GetSyntaxAsync(
            new EditorProviderContext(new TextSnapshot(text), 0, TextSelection.Caret(0)));

        Assert.Contains(
            spans,
            span => span.Classification == SyntaxClassifications.Link
                && text[span.Range.Start..span.Range.End] == "https://example.com/page");
    }
}
