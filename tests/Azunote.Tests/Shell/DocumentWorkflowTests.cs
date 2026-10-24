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
            SavePath = "renamed.txt"
        };
        files.Files[Path.GetFullPath("notes.txt")] = new TextFileData(
            "hello",
            TextEncoding.Utf8,
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
        Assert.Equal(TextEncoding.Utf8, workflow.CurrentEncoding);
        Assert.False(workflow.IsDirty);
    }

    [Fact]
    public async Task New_document_opens_a_new_window_without_touching_current_document()
    {
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var newWindowCount = 0;
        var workflow = CreateWorkflow(
            editor,
            session,
            files,
            prompt,
            new FakeFileDialogService(),
            createNewWindow: () =>
            {
                newWindowCount++;
                return Task.CompletedTask;
            });

        await workflow.OpenStartupTextAsync("draft");
        await workflow.NewDocumentAsync();

        Assert.Equal("draft", editor.Text);
        Assert.True(workflow.IsDirty);
        Assert.Equal(1, newWindowCount);
    }

    [Fact]
    public async Task Open_file_reuses_a_clean_untitled_window()
    {
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var dialogs = new FakeFileDialogService { OpenPath = "notes.txt" };
        files.Files[Path.GetFullPath("notes.txt")] = new TextFileData(
            "hello",
            TextEncoding.Utf8,
            LineEndingKind.Lf);
        var newWindowCount = 0;
        var workflow = CreateWorkflow(
            editor,
            session,
            files,
            prompt,
            dialogs,
            createNewWindow: () =>
            {
                newWindowCount++;
                return Task.CompletedTask;
            });

        await workflow.OpenFileAsync();

        Assert.Equal("hello", editor.Text);
        Assert.Equal(Path.GetFullPath("notes.txt"), workflow.CurrentFilePath);
        Assert.Equal("notes.txt", workflow.CurrentDocumentName);
        Assert.Equal(0, newWindowCount);
    }

    [Fact]
    public async Task Open_recent_file_uses_the_same_window_when_it_is_clean_untitled()
    {
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        files.Files[Path.GetFullPath("notes.txt")] = new TextFileData(
            "hello",
            TextEncoding.Utf8,
            LineEndingKind.Lf);
        var newWindowCount = 0;
        var workflow = CreateWorkflow(
            editor,
            session,
            files,
            prompt,
            new FakeFileDialogService(),
            createNewWindow: () =>
            {
                newWindowCount++;
                return Task.CompletedTask;
            });

        await workflow.OpenRecentFileAsync("notes.txt");

        Assert.Equal("hello", editor.Text);
        Assert.Equal(Path.GetFullPath("notes.txt"), workflow.CurrentFilePath);
        Assert.Equal(0, newWindowCount);
        Assert.Empty(prompt.Errors);
    }

    [Fact]
    public async Task Open_file_cancellation_leaves_the_current_window_unchanged()
    {
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var newWindowCount = 0;
        var workflow = CreateWorkflow(
            editor,
            session,
            files,
            prompt,
            new FakeFileDialogService(),
            createNewWindow: () =>
            {
                newWindowCount++;
                return Task.CompletedTask;
            });
        await workflow.OpenStartupTextAsync("draft");

        await workflow.OpenFileAsync();

        Assert.Equal("draft", editor.Text);
        Assert.Null(workflow.CurrentFilePath);
        Assert.Equal("Untitled", workflow.CurrentDocumentName);
        Assert.True(workflow.IsDirty);
        Assert.Equal(0, newWindowCount);
    }

    [Fact]
    public async Task Open_file_preserves_a_dirty_untitled_window_and_opens_a_new_one()
    {
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var dialogs = new FakeFileDialogService { OpenPath = "notes.txt" };
        var openedPath = string.Empty;
        var workflow = CreateWorkflow(
            editor,
            session,
            files,
            prompt,
            dialogs,
            openFileInNewWindow: path =>
            {
                openedPath = path;
                return Task.CompletedTask;
            });
        await workflow.OpenStartupTextAsync("draft");

        await workflow.OpenFileAsync();

        Assert.Equal("draft", editor.Text);
        Assert.Null(workflow.CurrentFilePath);
        Assert.True(workflow.IsDirty);
        Assert.Equal("notes.txt", openedPath);
    }

    [Fact]
    public async Task Open_file_uses_a_new_window_when_current_document_is_saved()
    {
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var dialogs = new FakeFileDialogService { OpenPath = "other.txt" };
        files.Files[Path.GetFullPath("current.txt")] = new TextFileData(
            "current",
            TextEncoding.Utf8,
            LineEndingKind.Lf);
        var openedPath = string.Empty;
        var workflow = CreateWorkflow(
            editor,
            session,
            files,
            prompt,
            dialogs,
            openFileInNewWindow: path =>
            {
                openedPath = path;
                return Task.CompletedTask;
            });

        dialogs.OpenPath = "current.txt";
        await workflow.OpenFileAsync();
        dialogs.OpenPath = "other.txt";
        await workflow.OpenFileAsync();

        Assert.Equal("current", editor.Text);
        Assert.Equal(Path.GetFullPath("current.txt"), workflow.CurrentFilePath);
        Assert.Equal("other.txt", openedPath);
    }

    [Fact]
    public async Task Open_file_applies_editorconfig_to_editor_and_session_settings()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote-workflow-editorconfig-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "sample.cs");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, ".editorconfig"),
                "root = true\n"
                + "[*.cs]\n"
                + "indent_style = space\n"
                + "indent_size = 2\n"
                + "tab_width = 8\n"
                + "end_of_line = crlf\n"
                + "charset = utf-8-bom\n");

            var editor = new FakeEditorView();
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var dialogs = new FakeFileDialogService { OpenPath = path };
            files.Files[Path.GetFullPath(path)] = new TextFileData(
                "first\nsecond",
                TextEncoding.Utf8,
                LineEndingKind.Lf);
            var workflow = CreateWorkflow(editor, session, files, prompt, dialogs);

            await workflow.OpenFileAsync();

            Assert.Equal(8, editor.TabDisplaySize);
            Assert.Equal(2, editor.IndentSize);
            Assert.Equal(IndentationInputMode.Spaces, editor.IndentationInputMode);
            Assert.Equal("\r\n", editor.PreferredLineEnding);
            Assert.Equal(TextEncoding.Utf8Bom, workflow.CurrentEncoding);
            Assert.Equal(LineEndingKind.CrLf, workflow.CurrentLineEnding);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_as_uses_target_editorconfig_but_keeps_the_dialog_encoding()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote-workflow-editorconfig-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "saved.txt");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, ".editorconfig"),
                "root = true\n"
                + "[*.txt]\n"
                + "indent_style = tab\n"
                + "indent_size = tab\n"
                + "tab_width = 8\n"
                + "end_of_line = lf\n"
                + "charset = utf-16le\n"
                + "insert_final_newline = true\n"
                + "trim_trailing_whitespace = true\n");

            var editor = new FakeEditorView();
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var dialogs = new FakeFileDialogService
            {
                SavePath = path
            };
            var workflow = CreateWorkflow(editor, session, files, prompt, dialogs);
            await workflow.OpenStartupTextAsync("first  \nsecond");

            Assert.True(await workflow.SaveAsAsync());

            Assert.Equal("first\nsecond\n", files.Files[Path.GetFullPath(path)].Text);
            Assert.Equal(TextEncoding.Utf8, workflow.CurrentEncoding);
            Assert.Equal(LineEndingKind.Lf, workflow.CurrentLineEnding);
            Assert.Equal(8, editor.TabDisplaySize);
            Assert.Equal(IndentationInputMode.Tab, editor.IndentationInputMode);
            Assert.Equal("\n", editor.PreferredLineEnding);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Deleted_file_marks_the_document_until_it_returns_unchanged()
    {
        var path = Path.GetTempFileName();
        try
        {
            var editor = new FakeEditorView();
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            files.Files[Path.GetFullPath(path)] = new TextFileData("text", TextEncoding.Utf8, LineEndingKind.Lf);
            var factory = new FakeFileChangeMonitorFactory();
            var workflow = CreateWorkflow(
                editor,
                session,
                files,
                new FakeUserPrompt(),
                new FakeFileDialogService { OpenPath = path },
                factory);
            await workflow.OpenFileAsync();

            File.Delete(path);
            factory.Monitors[^1].Trigger(path);
            await Task.Delay(30);
            Assert.True(session.State.IsDeletedOnDisk);
            Assert.True(workflow.IsDirty);

            File.WriteAllText(path, "text");
            factory.Monitors[^1].Trigger(path);
            await Task.Delay(30);
            Assert.False(session.State.IsDeletedOnDisk);
            Assert.False(workflow.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_file_deleted_while_saving_is_noticed_when_the_watch_restarts()
    {
        var root = Directory.CreateTempSubdirectory("azunote-save-delete-");
        var path = Path.Combine(root.FullName, "note.txt");
        File.WriteAllText(path, "text");
        try
        {
            var editor = new FakeEditorView();
            var session = new DocumentSession();
            var prompt = new FakeUserPrompt();
            var files = new DeletingAfterWriteFileStore();
            var documents = new DocumentController(editor, session, files, prompt);
            var workflow = new DocumentWorkflow(
                editor,
                documents,
                new ExternalToolController(editor, documents, files, prompt, (_, _) => Task.CompletedTask),
                new FakeFileDialogService { OpenPath = path },
                new DefaultFileChangeMonitorFactory(),
                new FakeUiDispatcher(),
                prompt,
                LanguageModeCatalog.Create().GetFileDialogFilters,
                () => "plain-text",
                () => Task.CompletedTask,
                _ => Task.CompletedTask);
            await workflow.OpenFileAsync();
            Assert.True(await workflow.SaveWithEncodingAsync(TextEncoding.Utf16LittleEndian));

            for (var attempt = 0; attempt < 40 && !session.State.IsDeletedOnDisk; attempt++)
            {
                await Task.Delay(50);
            }

            Assert.True(session.State.IsDeletedOnDisk, $"Errors: {string.Join(" | ", prompt.Errors)}");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Format_changes_stay_unsaved_until_the_document_is_saved_with_them()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt"));
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        files.Files[path] = new TextFileData("a\r\nb", TextEncoding.Utf8, LineEndingKind.CrLf);
        var workflow = CreateWorkflow(
            editor,
            session,
            files,
            new FakeUserPrompt(),
            new FakeFileDialogService { OpenPath = path });
        await workflow.OpenFileAsync();

        workflow.ChangeLineEnding(LineEndingKind.Lf);
        Assert.True(workflow.IsDirty);
        editor.Replace(new Azunyan.Core.TextRange(0, 0), "");
        workflow.ObserveTextChanged();
        Assert.True(workflow.IsDirty);

        Assert.True(await workflow.SaveWithEncodingAsync(TextEncoding.Utf16LittleEndian));
        Assert.Equal(TextEncoding.Utf16LittleEndian, files.Files[path].Encoding);
        Assert.Equal(LineEndingKind.Lf, files.Files[path].LineEnding);
        Assert.False(workflow.IsDirty);

        await workflow.ReopenWithEncodingAsync(TextEncoding.Latin1);
        Assert.Equal(TextEncoding.Latin1, session.State.Encoding);
        Assert.False(workflow.IsDirty);
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
                TextEncoding.Utf8,
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
                TextEncoding.Utf8,
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

    [Fact]
    public async Task File_change_reloads_a_clean_document_and_only_asks_once_dirty()
    {
        var path = Path.GetTempFileName();
        try
        {
            var editor = new FakeEditorView();
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt
            {
                ExternalDecision = ExternalChangeDecision.Keep
            };
            var dialogs = new FakeFileDialogService { OpenPath = path };
            var fullPath = Path.GetFullPath(path);
            files.Files[fullPath] = new TextFileData(
                "before",
                TextEncoding.Utf8,
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
            files.Files[fullPath] = new TextFileData(
                "after",
                TextEncoding.Utf8,
                LineEndingKind.Lf);
            factory.Monitors[^1].Trigger(path);
            await Task.Delay(30);

            // Nothing was at stake, so the reload happened on its own.
            Assert.Equal("after", editor.Text);
            Assert.Equal(0, prompt.ExternalDecisionCount);
            Assert.False(workflow.IsDirty);

            editor.Replace(new TextRange(5, 0), "!");
            workflow.ObserveTextChanged();
            Assert.True(workflow.IsDirty);
            files.Files[fullPath] = new TextFileData(
                "third",
                TextEncoding.Utf8,
                LineEndingKind.Lf);
            factory.Monitors[^1].Trigger(path);
            await Task.Delay(30);

            Assert.Equal(1, prompt.ExternalDecisionCount);
            Assert.Equal("after!", editor.Text);
            Assert.True(workflow.IsDirty);
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
        FakeFileChangeMonitorFactory? factory = null,
        Func<Task>? createNewWindow = null,
        Func<string, Task>? openFileInNewWindow = null)
    {
        var documents = new DocumentController(editor, session, files, prompt);
        var externalTools = new ExternalToolController(
            editor,
            documents,
            files,
            prompt,
            (_, _) => Task.CompletedTask);
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
            () => "plain-text",
            createNewWindow ?? (() => Task.CompletedTask),
            openFileInNewWindow ?? (_ => Task.CompletedTask));
    }

    /// <summary>Removes the file right after writing it, before the save restarts its watch.</summary>
    private sealed class DeletingAfterWriteFileStore : ITextFileStore
    {
        private readonly TextFileStore _files = new();

        public Task<TextFileData> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            _files.ReadAsync(path, cancellationToken);

        public Task<TextFileData> ReadAsync(
            string path,
            TextEncoding? encodingHint,
            CancellationToken cancellationToken = default) =>
            _files.ReadAsync(path, encodingHint, cancellationToken);

        public Task<TextFileData> ReadWithEncodingAsync(
            string path,
            TextEncoding encoding,
            CancellationToken cancellationToken = default) =>
            _files.ReadWithEncodingAsync(path, encoding, cancellationToken);

        public async Task WriteAsync(
            string path,
            string text,
            TextEncoding encoding,
            LineEndingKind lineEnding,
            CancellationToken cancellationToken = default)
        {
            await _files.WriteAsync(path, text, encoding, lineEnding, cancellationToken);
            File.Delete(path);
        }
    }
}
