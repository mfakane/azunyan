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
            var controller = new ExternalToolController(editor, documents, files, prompt);

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
            var documents = new DocumentController(editor, session, files, prompt);
            var controller = new ExternalToolController(editor, documents, files, prompt);

            var result = await controller.RunAsync(
                new ExternalToolDefinition(
                    scriptPath,
                    stderr: new ExternalToolOutputActions(
                        ExternalToolOutputMode.Ignore,
                        ExternalToolOutputMode.NewDocument)));

            Assert.Equal(3, result.ExitCode);
            Assert.Equal("external failure", editor.Text.Trim());
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
