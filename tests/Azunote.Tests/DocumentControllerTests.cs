using Azunyan.Core;
using Xunit;

namespace Azunote.Tests;

public sealed class DocumentControllerTests
{
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
    }

    private sealed class FakeTextFileStore : ITextFileStore
    {
        public Dictionary<string, TextFileData> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<TextFileData> ReadAsync(string path, CancellationToken cancellationToken = default) =>
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
