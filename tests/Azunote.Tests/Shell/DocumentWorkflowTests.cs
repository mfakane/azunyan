using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class DocumentWorkflowTests
{
    [Fact]
    public async Task Open_save_and_save_as_keep_workflow_state_consistent()
    {
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var dialogs = new FakeFileDialogService
        {
            OpenPath = "notes.txt",
            SaveResult = new SaveFileDialogResult(
                "renamed.txt",
                TextEncodingKind.Utf8Bom,
                LineEndingKind.CrLf)
        };
        files.Files[Path.GetFullPath("notes.txt")] = new TextFileData(
            "hello",
            TextEncodingKind.Utf8,
            LineEndingKind.Lf);
        var workflow = CreateWorkflow(editor, session, files, prompt, dialogs);

        await workflow.OpenFileAsync();
        Assert.Equal("hello", editor.Text);
        Assert.Equal(Path.GetFullPath("notes.txt"), workflow.CurrentFilePath);
        Assert.False(workflow.IsDirty);

        editor.Replace(new TextRange(5, 0), "!");
        workflow.ObserveTextChanged();
        Assert.True(await workflow.SaveAsync());
        Assert.False(workflow.IsDirty);
        Assert.Equal("hello!", files.Files[Path.GetFullPath("notes.txt")].Text);

        editor.Replace(new TextRange(6, 0), "?");
        workflow.ObserveTextChanged();
        Assert.True(await workflow.SaveAsAsync());
        Assert.Equal(Path.GetFullPath("renamed.txt"), workflow.CurrentFilePath);
        Assert.Equal(TextEncodingKind.Utf8Bom, workflow.CurrentEncoding);
        Assert.False(workflow.IsDirty);
    }

    [Fact]
    public async Task New_document_respects_cancelled_pending_changes()
    {
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt
        {
            PendingDecision = PendingChangesDecision.Cancel
        };
        var workflow = CreateWorkflow(
            editor,
            session,
            files,
            prompt,
            new FakeFileDialogService());

        await workflow.OpenStartupTextAsync("draft");
        await workflow.NewDocumentAsync();

        Assert.Equal("draft", editor.Text);
        Assert.True(workflow.IsDirty);
    }

    [Fact]
    public async Task File_change_reload_uses_current_monitor_and_prompt_decision()
    {
        var path = Path.GetTempFileName();
        try
        {
            var editor = new FakeEditorView();
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt
            {
                ExternalDecision = ExternalChangeDecision.Reload
            };
            var dialogs = new FakeFileDialogService { OpenPath = path };
            files.Files[Path.GetFullPath(path)] = new TextFileData(
                "before",
                TextEncodingKind.Utf8,
                LineEndingKind.Lf);
            var factory = new FakeFileChangeMonitorFactory();
            var workflow = CreateWorkflow(
                editor,
                session,
                files,
                prompt,
                dialogs,
                factory);

            await workflow.OpenFileAsync();
            files.Files[Path.GetFullPath(path)] = new TextFileData(
                "after",
                TextEncodingKind.Utf8,
                LineEndingKind.Lf);
            factory.Monitors[^1].Trigger(path);
            await Task.Delay(30);

            Assert.Equal("after", editor.Text);
            Assert.False(workflow.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static DocumentWorkflow CreateWorkflow(
        FakeEditorView editor,
        DocumentSession session,
        FakeTextFileStore files,
        FakeUserPrompt prompt,
        FakeFileDialogService dialogs,
        FakeFileChangeMonitorFactory? factory = null)
    {
        var documents = new DocumentController(editor, session, files, prompt);
        var externalTools = new ExternalToolController(editor, documents, files, prompt);
        var modes = LanguageModeCatalog.Create();
        return new DocumentWorkflow(
            editor,
            documents,
            externalTools,
            dialogs,
            factory ?? new FakeFileChangeMonitorFactory(),
            new FakeUiDispatcher(),
            prompt,
            modes.GetFileDialogFilters,
            () => "plain-text");
    }
}
