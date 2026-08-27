using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class DocumentStatusPresenterTests
{
    [Fact]
    public void Refresh_projects_position_file_metadata_and_title()
    {
        var editor = new FakeEditorView("first\nsecond");
        editor.SetSelection(TextSelection.Caret(6));
        var session = new DocumentSession();
        session.Load(
            "notes.txt",
            new TextFileData(
                "first\nsecond",
                TextEncodingKind.Utf8Bom,
                LineEndingKind.Lf));
        var status = new FakeStatusBarView();
        var chrome = new FakeWindowChromeView();
        var presenter = new DocumentStatusPresenter(editor, session, status, chrome);

        editor.SetTabDisplaySize(4);
        editor.SetIndentSize(8);
        presenter.Refresh();
        presenter.RefreshTitle();

        Assert.Equal("Ln 2, Col 1", status.State?.Position);
        Assert.Equal("UTF-8 BOM", status.State?.Encoding);
        Assert.Equal("LF", status.State?.LineEnding);
        Assert.Equal("Spaces: 8", status.State?.Indentation);
        Assert.Equal("notes.txt - Azunote", chrome.Title);
    }

    [Fact]
    public void Refresh_uses_tab_display_size_for_tab_indentation()
    {
        var editor = new FakeEditorView("\tvalue");
        editor.SetSelection(TextSelection.Caret(1));
        var session = new DocumentSession();
        session.Load(
            "notes.txt",
            new TextFileData("\tvalue", TextEncodingKind.Utf8, LineEndingKind.Lf));
        var status = new FakeStatusBarView();
        var chrome = new FakeWindowChromeView();
        var presenter = new DocumentStatusPresenter(editor, session, status, chrome);

        editor.SetTabDisplaySize(8);
        editor.SetIndentSize(2);
        presenter.Refresh();

        Assert.Equal("Tab Size: 8", status.State?.Indentation);
    }
}
