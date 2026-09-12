using Azunote;
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
                _ => Task.CompletedTask);

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
            var documents = new DocumentController(editor, session, files, prompt);
            var controller = new ExternalToolController(
                editor,
                documents,
                files,
                prompt,
                text =>
                {
                    openedText = text;
                    return Task.CompletedTask;
                });

            var result = await controller.RunAsync(
                new ExternalToolDefinition(
                    scriptPath,
                    stderr: new ExternalToolOutputActions(
                        ExternalToolOutputMode.Ignore,
                        ExternalToolOutputMode.NewDocument)));

            Assert.Equal(3, result.ExitCode);
            Assert.Equal("before", editor.Text);
            Assert.Equal("external failure", openedText.Trim());
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
                _ => Task.CompletedTask);

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
}
