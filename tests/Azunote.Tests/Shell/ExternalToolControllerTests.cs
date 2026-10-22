using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class ExternalToolControllerTests
{
    [Fact]
    public async Task Mixed_output_action_replaces_the_document()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external controller {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "write output.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\necho mixed output\r\n");

            var editor = new FakeEditorView("before");
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var documents = new DocumentController(editor, session, files, prompt);
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                (_, _) => Task.CompletedTask);

            var result = await controller.RunAsync(
                new ExternalToolDefinition(
                    scriptPath,
                    output: new ExternalToolOutputActions(
                        ExternalToolOutputMode.ReplaceDocument,
                        ExternalToolOutputMode.Ignore)));

            Assert.True(result.Succeeded, result.StandardError);
            Assert.Equal("mixed output\r\n", editor.Text);
            Assert.Empty(prompt.Errors);
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
    public async Task Replace_selection_action_keeps_the_replacement_selected()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external controller selection {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "write selection.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\n<nul set /p =REPLACED\r\nexit /b 0\r\n");

            var editor = new FakeEditorView("one two three");
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var documents = new DocumentController(editor, session, files, prompt);
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                (_, _) => Task.CompletedTask);
            var definition = new ExternalToolDefinition(
                scriptPath,
                stdout: new ExternalToolOutputActions(
                    ExternalToolOutputMode.ReplaceSelection,
                    ExternalToolOutputMode.Ignore));

            editor.SetSelection(new TextSelection(4, 7));
            var result = await controller.RunAsync(definition);

            Assert.True(result.Succeeded, result.StandardError);
            Assert.Equal("one REPLACED three", editor.Text);
            Assert.Equal(new TextSelection(4, 12), editor.Selection);
            Assert.Equal("REPLACED", editor.SelectedText);

            editor.SetText("one two three");
            editor.SetSelection(new TextSelection(7, 4));
            await controller.RunAsync(definition);

            Assert.Equal(new TextSelection(12, 4), editor.Selection);
            Assert.Equal("REPLACED", editor.SelectedText);

            // Output inserted at a caret leaves the caret after it.
            editor.SetText("one two three");
            editor.SetSelection(TextSelection.Caret(4));
            await controller.RunAsync(definition);

            Assert.Equal("one REPLACEDtwo three", editor.Text);
            Assert.Equal(TextSelection.Caret(12), editor.Selection);
            Assert.Empty(prompt.Errors);
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
    public async Task Diagnostics_action_places_the_problems_and_a_clean_run_clears_them()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external controller diagnostics {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "lint.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\n"
                + "if exist \"%~dp0clean\" exit /b 0\r\n"
                + "echo -:2:5: warning: unused\r\n"
                + "echo other.txt:1:1: error: elsewhere\r\n"
                + "exit /b 1\r\n");

            var editor = new FakeEditorView("one\r\ntwo three");
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var documents = new DocumentController(editor, session, files, prompt);
            var diagnostics = new DocumentDiagnostics();
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                (_, _) => Task.CompletedTask,
                languageModeId: null,
                warmPool: null,
                diagnostics: diagnostics);
            var diagnosticsOutput = new ExternalToolOutputActions(
                ExternalToolOutputMode.Diagnostics,
                ExternalToolOutputMode.Diagnostics);

            var definition = new ExternalToolDefinition(scriptPath, stdout: diagnosticsOutput);
            await controller.RunAsync(definition, "Lint");

            var problem = Assert.Single(diagnostics.Items);
            Assert.Equal(TextRange.FromBounds(9, 14), problem.Range);
            Assert.Equal((DiagnosticSeverity.Warning, "unused", "Lint"), (problem.Severity, problem.Message, problem.Source));

            await File.WriteAllTextAsync(Path.Combine(root, "clean"), string.Empty);
            await controller.RunAsync(definition, "Lint");

            Assert.Empty(diagnostics.Items);
            Assert.Empty(prompt.Errors);
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
    public async Task A_proposed_selection_is_applied_where_the_selection_moved_unless_it_was_edited()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"azunote external controller proposal {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "upper.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(scriptPath, "@echo off\r\n<nul set /p =TWO\r\nexit /b 0\r\n");

            var editor = new FakeEditorView("one two three");
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var documents = new DocumentController(editor, session, files, prompt);
            var proposals = new List<ExternalToolProposal>();
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                (_, _) => Task.CompletedTask,
                languageModeId: null,
                warmPool: null,
                openProposal: proposal =>
                {
                    proposals.Add(proposal);
                    return Task.CompletedTask;
                });
            var definition = new ExternalToolDefinition(
                scriptPath,
                stdout: new ExternalToolOutputActions(
                    ExternalToolOutputMode.ProposeSelection,
                    ExternalToolOutputMode.Ignore));

            editor.SetSelection(new TextSelection(4, 7));
            await controller.RunAsync(definition, "Upper");

            var proposal = Assert.Single(proposals);
            Assert.Equal("one two three", editor.Text);
            Assert.Equal("--- a/Untitled\n+++ b/Untitled\n@@ -1 +1 @@\n-two\n+TWO\n", proposal.Diff);
            // Typing before the selection moves it; the change follows.
            editor.Replace(new TextRange(0, 0), ">");
            Assert.Null(proposal.Apply());
            Assert.Equal(">one TWO three", editor.Text);

            // Editing inside the selection keeps the change from applying.
            editor.SetSelection(new TextSelection(9, 14));
            await controller.RunAsync(definition, "Upper");
            editor.Replace(new TextRange(10, 1), "x");

            Assert.NotNull(proposals[1].Apply());
            Assert.Equal(">one TWO txree", editor.Text);
            Assert.Empty(prompt.Errors);
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
    public async Task Stderr_failure_action_can_open_a_new_document_without_an_error_prompt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external controller failure {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "write error.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\necho external failure 1>&2\r\nexit /b 3\r\n");

            var editor = new FakeEditorView("before");
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var openedText = string.Empty;
            string? openedLanguage = null;
            var documents = new DocumentController(editor, session, files, prompt);
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                (text, language) =>
                {
                    openedText = text;
                    openedLanguage = language;
                    return Task.CompletedTask;
                });

            var result = await controller.RunAsync(
                new ExternalToolDefinition(
                    scriptPath,
                    stderr: new ExternalToolOutputActions(
                        ExternalToolOutputMode.Ignore,
                        ExternalToolOutputMode.NewDocument,
                        OnFailureLanguage: "diff")));

            Assert.Equal(3, result.ExitCode);
            Assert.Equal("before", editor.Text);
            Assert.Equal("external failure", openedText.Trim());
            Assert.Equal("diff", openedLanguage);
            Assert.Empty(prompt.Errors);
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
    public async Task Show_completion_action_uses_non_empty_output_lines_as_candidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external controller completion {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "write completion.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\necho first\r\necho.\r\necho second\r\n");

            var editor = new FakeEditorView("before");
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var documents = new DocumentController(editor, session, files, prompt);
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                (_, _) => Task.CompletedTask);

            var result = await controller.RunAsync(
                new ExternalToolDefinition(
                    scriptPath,
                    stdout: new ExternalToolOutputActions(
                        ExternalToolOutputMode.ShowCompletion,
                        ExternalToolOutputMode.Ignore)));

            Assert.True(result.Succeeded, result.StandardError);
            Assert.NotNull(editor.LastCompletion);
            Assert.Equal(
                ["first", "second"],
                editor.LastCompletion!.Items.Select(item => item.InsertText));
            Assert.Empty(prompt.Errors);
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
    public async Task Streamed_replace_selection_applies_the_same_text_it_would_have_buffered()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external controller stream selection {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "write lines.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\necho A\r\necho B\r\n");

            var editor = new FakeEditorView("one two three");
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var documents = new DocumentController(editor, session, files, prompt);
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                (_, _) => Task.CompletedTask);
            var definition = new ExternalToolDefinition(
                scriptPath,
                stdout: new ExternalToolOutputActions(
                    ExternalToolOutputMode.ReplaceSelection,
                    ExternalToolOutputMode.ReplaceSelection),
                stream: ExternalToolStreamChannels.Stdout);

            editor.SetSelection(new TextSelection(4, 7));
            var result = await controller.RunAsync(definition);

            Assert.True(result.Succeeded, result.StandardError);
            Assert.Equal("one A\r\nB\r\n three", editor.Text);
            Assert.Equal(result.StandardOutput, editor.SelectedText);
            Assert.Empty(prompt.Errors);
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
    public async Task Streamed_new_document_appends_to_a_window_opened_for_the_run()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external controller stream document {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "write lines.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\necho A\r\necho B\r\n");

            var editor = new FakeEditorView("before");
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var documents = new DocumentController(editor, session, files, prompt);
            var streamed = new FakeStreamedDocument();
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                (_, _) => throw new InvalidOperationException("The streamed window should have been used."),
                languageModeId: null,
                warmPool: null,
                languageExtensions: null,
                dispatcher: null,
                openStreamedDocument: _ => Task.FromResult<IExternalToolDocument>(streamed));

            var result = await controller.RunAsync(
                new ExternalToolDefinition(
                    scriptPath,
                    stdout: new ExternalToolOutputActions(
                        ExternalToolOutputMode.NewDocument,
                        ExternalToolOutputMode.NewDocument),
                    stream: ExternalToolStreamChannels.Stdout));

            Assert.True(result.Succeeded, result.StandardError);
            Assert.Equal(result.StandardOutput, streamed.Text);
            Assert.True(streamed.Completed);
            Assert.Equal("before", editor.Text);
            Assert.Empty(prompt.Errors);
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
    public async Task A_streamed_run_that_never_invoked_the_tool_leaves_the_document_alone()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external controller stream empty {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "write lines.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(scriptPath, "@echo off\r\necho A\r\n");

            var editor = new FakeEditorView("one two three");
            var session = new DocumentSession();
            var files = new FakeTextFileStore();
            var prompt = new FakeUserPrompt();
            var documents = new DocumentController(editor, session, files, prompt);
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                (_, _) => Task.CompletedTask);

            editor.SetSelection(new TextSelection(4, 7));
            var result = await controller.RunAsync(
                new ExternalToolDefinition(
                    scriptPath,
                    inputMode: ExternalToolInputMode.Selection,
                    per: "regex:zzz",
                    stdout: new ExternalToolOutputActions(
                        ExternalToolOutputMode.ReplaceSelection,
                        ExternalToolOutputMode.ReplaceSelection),
                    stream: ExternalToolStreamChannels.Stdout));

            Assert.Equal(0, result.InvocationCount);
            Assert.Equal("one two three", editor.Text);
            Assert.Empty(prompt.Errors);
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
    public async Task A_streamed_run_keeps_writing_where_it_left_off_when_the_document_is_edited()
    {
        if (!OperatingSystem.IsWindows()
            || ExternalToolLaunchResolver.ResolvePowerShell() is null)
        {
            return;
        }

        var editor = new FakeEditorView("one two three");
        var session = new DocumentSession();
        var files = new FakeTextFileStore();
        var prompt = new FakeUserPrompt();
        var documents = new DocumentController(editor, session, files, prompt);
        var dispatcher = new QueuedUiDispatcher();
        var controller = new ExternalToolController(
            editor,
            documents,
            files,
            prompt,
            (_, _) => Task.CompletedTask,
            languageModeId: null,
            warmPool: null,
            languageExtensions: null,
            dispatcher: dispatcher);
        var replaceSelection = new ExternalToolOutputActions(
            ExternalToolOutputMode.ReplaceSelection,
            ExternalToolOutputMode.ReplaceSelection);

        editor.SetSelection(new TextSelection(4, 7));
        var run = controller.RunAsync(
            new ExternalToolDefinition(
                "'A'; Start-Sleep -Milliseconds 1500; 'B'",
                commandMode: ExternalToolCommandMode.Pwsh,
                stdout: replaceSelection,
                stream: ExternalToolStreamChannels.Stdout));

        // Everything the run applies goes through the dispatcher, so draining
        // it here is what puts the edits and this test on one thread.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var edited = false;
        while (!run.IsCompleted && DateTime.UtcNow < deadline)
        {
            dispatcher.Drain();
            if (!edited && editor.Text != "one two three")
            {
                editor.Replace(new TextRange(0, 0), "XX");
                edited = true;
            }

            await Task.Delay(10);
        }

        dispatcher.Drain();
        var result = await run;

        Assert.True(edited, "The run applied nothing before the tool exited.");
        Assert.True(result.Succeeded, result.StandardError);
        var expected = "A" + Environment.NewLine + "B";
        Assert.Equal("XXone " + expected + " three", editor.Text);
        Assert.Equal(expected, editor.SelectedText);
        Assert.Empty(prompt.Errors);
    }
}

internal sealed class FakeStreamedDocument : IExternalToolDocument
{
    private readonly System.Text.StringBuilder _text = new();

    public string Text => _text.ToString();

    public bool Completed { get; private set; }

    public void Append(string text) => _text.Append(text);

    public void Complete() => Completed = true;
}

internal sealed class QueuedUiDispatcher : IUiDispatcher
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _queue = new();

    public bool TryEnqueue(Action action)
    {
        _queue.Enqueue(action);
        return true;
    }

    public void Drain()
    {
        while (_queue.TryDequeue(out var action))
        {
            action();
        }
    }
}
