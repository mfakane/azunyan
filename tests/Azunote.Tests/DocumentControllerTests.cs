using Azunyan.Core;
using Xunit;

namespace Azunote.Tests;

public sealed class DocumentControllerTests
{
    [Theory]
    [InlineData("LICENSE")]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    [InlineData("licenses/Tomlyn/2.10.1/LICENSE.txt")]
    public async Task Bundled_legal_documents_cannot_be_saved_and_reload_preserves_read_only(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
        var editor = new FakeEditorBuffer();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var controller = new DocumentController(editor, session, files, new FakeUserPrompt());
        files.Files[path] = new TextFileData("original license", TextEncodingKind.Utf8, LineEndingKind.Lf);

        await controller.OpenAsync(path);
        Assert.True(session.State.IsReadOnly);
        Assert.Equal("original license", editor.Text);
        editor.SetText("host update");
        Assert.False(await controller.SaveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SaveAsAsync(
            new SaveFileDialogResult("copy.txt", TextEncodingKind.Utf8, LineEndingKind.Lf)));
        Assert.Equal("original license", files.Files[path].Text);
        Assert.Single(files.Files);

        Assert.True(await controller.ReloadFromDiskAsync());
        Assert.True(session.State.IsReadOnly);
        Assert.Equal("original license", editor.Text);
        controller.NewDocument();
        Assert.False(session.State.IsReadOnly);
    }

    [Fact]
    public async Task Save_as_cannot_overwrite_a_bundled_license_from_an_editable_document()
    {
        var files = new FakeTextFileStore();
        var controller = new DocumentController(
            new FakeEditorBuffer(), new DocumentSession(), files, new FakeUserPrompt());
        controller.LoadUntitledText("draft");

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SaveAsAsync(
            new SaveFileDialogResult(Path.Combine(AppContext.BaseDirectory, "LICENSE"),
                TextEncodingKind.Utf8, LineEndingKind.Lf)));

        Assert.Empty(files.Files);
        Assert.True(controller.Session.State.IsDirty);
    }

    [Theory]
    [InlineData("LICENSE", true)]
    [InlineData("third-party-notices.MD", true)]
    [InlineData("licenses/package/NOTICE.txt", true)]
    [InlineData("licenses/../notes.txt", false)]
    [InlineData("licenses-other/NOTICE.txt", false)]
    [InlineData("docs/LICENSE", false)]
    public void Only_bundled_legal_paths_are_read_only(string relativePath, bool expected)
    {
        var root = Path.GetFullPath("legal-path-test");
        Assert.Equal(expected, BundledLegalDocuments.IsReadOnlyPath(Path.Combine(root, relativePath), root));
    }

    [Fact]
    public async Task Open_and_save_round_trip_through_the_session_and_editor()
    {
        var editor = new FakeEditorBuffer();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var controller = new DocumentController(editor, session, files, prompt);
        files.Files[Path.GetFullPath("notes.txt")] =
            new TextFileData("hello", TextEncodingKind.Utf8, LineEndingKind.Lf);

        await controller.OpenAsync("notes.txt");
        Assert.Equal("hello", editor.Text);
        Assert.False(session.State.IsDirty);

        editor.Replace(new TextRange(5, 0), "!");
        session.ObserveText(editor.Text);
        Assert.True(session.State.IsDirty);

        Assert.True(await controller.SaveAsync());
        Assert.False(session.State.IsDirty);
        Assert.Equal("hello!", files.Files[Path.GetFullPath("notes.txt")].Text);
    }

    [Fact]
    public async Task Pending_save_can_be_cancelled_or_discarded_without_replacing_text()
    {
        var editor = new FakeEditorBuffer();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt { PendingDecision = PendingChangesDecision.Cancel };
        var controller = new DocumentController(editor, session, files, prompt);
        controller.LoadUntitledText("draft");

        Assert.False(await controller.ConfirmPendingChangesAsync(() => Task.FromResult(true)));
        Assert.Equal("draft", editor.Text);

        prompt.PendingDecision = PendingChangesDecision.Discard;
        Assert.True(await controller.ConfirmPendingChangesAsync(() => Task.FromResult(true)));
    }

    [Fact]
    public async Task External_change_keeps_local_text_until_reload_is_confirmed()
    {
        var editor = new FakeEditorBuffer();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt
        {
            ExternalDecision = ExternalChangeDecision.Keep
        };
        var controller = new DocumentController(editor, session, files, prompt);
        session.Load(
            "notes.txt",
            new TextFileData("saved", TextEncodingKind.Utf8, LineEndingKind.Lf));
        editor.SetText("local");
        session.ObserveText(editor.Text);

        Assert.False(await controller.ApplyExternalChangeAsync(
            new TextFileData("external", TextEncodingKind.Utf8, LineEndingKind.Lf)));
        Assert.Equal("local", editor.Text);

        prompt.ExternalDecision = ExternalChangeDecision.Reload;
        Assert.True(await controller.ApplyExternalChangeAsync(
            new TextFileData("external", TextEncodingKind.Utf8, LineEndingKind.Lf)));
        Assert.Equal("external", editor.Text);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public async Task Save_applies_editorconfig_save_rules_and_updates_the_editor()
    {
        var editor = new FakeEditorBuffer();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var controller = new DocumentController(editor, session, files, prompt);
        session.Load(
            "notes.txt",
            new TextFileData("first  \nsecond", TextEncodingKind.Utf8, LineEndingKind.Lf));
        editor.SetText("first  \nsecond");
        session.ObserveText(editor.Text);
        var settings = new EditorConfigSettings(
            LineEnding: LineEndingKind.Lf,
            InsertFinalNewline: true,
            TrimTrailingWhitespace: true);
        session.ApplyEditorConfig(settings);

        Assert.True(await controller.SaveAsync(editorConfig: settings));

        Assert.Equal("first\nsecond\n", editor.Text);
        Assert.Equal("first\nsecond\n", files.Files[Path.GetFullPath("notes.txt")].Text);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public async Task SaveAs_uses_the_dialog_encoding_and_line_ending_over_editorconfig()
    {
        var editor = new FakeEditorBuffer();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var controller = new DocumentController(editor, session, files, prompt);
        editor.SetText("first  \nsecond");
        var settings = new EditorConfigSettings(
            Encoding: TextEncodingKind.Utf16BigEndian,
            LineEnding: LineEndingKind.CrLf,
            InsertFinalNewline: true,
            TrimTrailingWhitespace: true);

        await controller.SaveAsAsync(
            new SaveFileDialogResult(
                "saved.txt",
                TextEncodingKind.Utf8,
                LineEndingKind.Lf),
            editorConfig: settings);

        var saved = files.Files[Path.GetFullPath("saved.txt")];
        Assert.Equal(TextEncodingKind.Utf8, saved.Encoding);
        Assert.Equal(LineEndingKind.Lf, saved.LineEnding);
        Assert.Equal("first\nsecond\n", saved.Text);
        Assert.Equal(TextEncodingKind.Utf8, session.State.Encoding);
        Assert.Equal(LineEndingKind.Lf, session.State.LineEnding);
    }

    [Fact]
    public async Task External_change_reload_joins_the_undo_history()
    {
        var editor = new FakeEditorBuffer();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var controller = new DocumentController(editor, session, files, new FakeUserPrompt());
        session.Load(
            "notes.txt",
            new TextFileData("saved", TextEncodingKind.Utf8, LineEndingKind.Lf));
        editor.SetText("saved");
        session.ObserveText(editor.Text);
        Assert.False(session.State.IsDirty);

        Assert.True(await controller.ApplyExternalChangeAsync(
            new TextFileData("external", TextEncodingKind.Utf8, LineEndingKind.Lf)));
        Assert.Equal("external", editor.Text);
        Assert.False(session.State.IsDirty);

        Assert.True(editor.Undo());
        Assert.Equal("saved", editor.Text);
        session.ObserveText(editor.Text);
        Assert.True(session.State.IsDirty);
    }

    [Fact]
    public async Task Disk_and_tool_reloads_join_the_undo_history()
    {
        var editor = new FakeEditorBuffer();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var controller = new DocumentController(editor, session, files, new FakeUserPrompt());
        files.Files[Path.GetFullPath("notes.txt")] = new TextFileData(
            "saved",
            TextEncodingKind.Utf8,
            LineEndingKind.Lf);
        files.Files[Path.GetFullPath("tool-output.txt")] = new TextFileData(
            "tooled",
            TextEncodingKind.Utf8,
            LineEndingKind.Lf);
        await controller.OpenAsync("notes.txt");

        files.Files[Path.GetFullPath("notes.txt")] = new TextFileData(
            "changed",
            TextEncodingKind.Utf8,
            LineEndingKind.Lf);
        Assert.True(await controller.ReloadFromDiskAsync());
        Assert.Equal("changed", editor.Text);
        Assert.True(editor.Undo());
        Assert.Equal("saved", editor.Text);

        // An external tool's reloadFile leaves its result on the same stack.
        Assert.True(await controller.ReloadFromTemporaryFileAsync("tool-output.txt"));
        Assert.Equal("tooled", editor.Text);
        Assert.True(editor.Undo());
        Assert.Equal("saved", editor.Text);
    }

    private sealed class FakeEditorBuffer : IEditorBuffer
    {
        private Document _document = new();

        public string Text => _document.Text;

        public string SelectedText => _document.Snapshot.GetText(Selection.Range);

        public TextSnapshot Snapshot => _document.Snapshot;

        public TextSelection Selection => _document.Selection;

        public int CaretPosition => _document.CaretPosition;

        public void SetText(string text) => _document = new Document(text);

        public void SetSelection(TextSelection selection) => _document.Selection = selection;

        public void Replace(TextRange range, string replacement) => _document.Replace(range, replacement);

        public bool Undo() => _document.Undo();
    }

    private sealed class FakeTextFileStore : ITextFileStore
    {
        public Dictionary<string, TextFileData> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<TextFileData> ReadAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Files[Path.GetFullPath(path)]);

        public Task<TextFileData> ReadAsync(
            string path,
            TextEncodingKind? encodingHint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Files[Path.GetFullPath(path)]);

        public Task WriteAsync(
            string path,
            string text,
            TextEncodingKind encoding,
            LineEndingKind lineEnding,
            CancellationToken cancellationToken = default)
        {
            Files[Path.GetFullPath(path)] = new TextFileData(text, encoding, lineEnding);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserPrompt : IUserPrompt
    {
        public PendingChangesDecision PendingDecision { get; set; } = PendingChangesDecision.Cancel;

        public ExternalChangeDecision ExternalDecision { get; set; } = ExternalChangeDecision.Reload;

        public Task<PendingChangesDecision> ConfirmPendingChangesAsync() =>
            Task.FromResult(PendingDecision);

        public Task<ExternalChangeDecision> ResolveExternalChangeAsync() =>
            Task.FromResult(ExternalDecision);

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
    }
}
