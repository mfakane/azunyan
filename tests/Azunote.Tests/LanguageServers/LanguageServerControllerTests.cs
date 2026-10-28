using System.Text.Json.Nodes;
using Azunote;
using Azunote.Tests.Shell;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.LanguageServers;

public sealed class LanguageServerControllerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"azunote-language-server-{Guid.NewGuid():N}");
    private readonly HashSet<(string Workspace, string Command)> _enabled = [];
    private readonly FakeEditorView _editor = new("let value = 1;");
    private readonly DocumentDiagnostics _diagnostics = new();
    private FakeLanguageServer _server = null!;
    private LanguageServerRegistry _registry = null!;
    private LanguageServerDefinition? _definition;
    private string? _filePath;

    private string Workspace => Path.Combine(_root, "app");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Workspace, ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        File.WriteAllText(Path.Combine(_root, "bin", "fake-server.exe"), string.Empty);
        _filePath = Path.Combine(Workspace, "app.ts");
        _definition = new LanguageServerDefinition(
            "fake-server",
            ["--stdio", "${workspaceFolderBasename}"],
            [Path.Combine(_root, "bin")],
            null,
            null,
            null,
            null);
        _server = new FakeLanguageServer();
        _registry = new LanguageServerRegistry(
            (workspace, command) => _enabled.Contains((workspace, command)),
            (workspace, command, enabled) =>
            {
                if (enabled)
                {
                    _enabled.Add((workspace, command));
                }
                else
                {
                    _enabled.Remove((workspace, command));
                }
            },
            (launch, _) =>
            {
                Launches.Add(launch);
                return _server.CreateClient(launch.WorkspaceFolder);
            });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _registry.Dispose();
        await _server.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private List<LanguageServerLaunch> Launches { get; } = [];

    [Fact]
    public void Nothing_shows_without_a_server_a_file_or_a_workspace_folder()
    {
        using var controller = CreateController();
        var definition = _definition;

        _definition = null;
        controller.Update();
        Assert.Equal(LanguageServerIndicatorState.Hidden, controller.Indicator.State);

        _definition = definition;
        _filePath = null;
        controller.Update();
        Assert.Equal(LanguageServerIndicatorState.Hidden, controller.Indicator.State);

        _filePath = Path.Combine(Path.GetPathRoot(_root)!, $"no-workspace-{Guid.NewGuid():N}", "app.ts");
        controller.Update();
        Assert.Equal(LanguageServerIndicatorState.Hidden, controller.Indicator.State);
    }

    [Fact]
    public async Task A_server_allowed_outside_workspaces_is_turned_on_for_all_such_files()
    {
        _definition = _definition! with { AllowOutsideWorkspace = true };
        _filePath = Path.Combine(Path.GetPathRoot(_root)!, $"no-workspace-{Guid.NewGuid():N}", "app.ts");
        using var controller = CreateController();
        controller.Update();
        Assert.Equal(LanguageServerIndicatorState.Off, controller.Indicator.State);
        Assert.True(controller.Indicator.OutsideWorkspace);

        controller.Toggle();
        await WaitForAsync(() => controller.Indicator.State == LanguageServerIndicatorState.Running);

        Assert.Contains((string.Empty, "fake-server"), _enabled);
        Assert.Equal(string.Empty, Assert.Single(Launches).WorkspaceFolder);
    }

    [Fact]
    public async Task Turning_the_server_on_starts_it_for_the_folder_and_opens_the_document()
    {
        using var controller = CreateController();
        controller.Update();
        Assert.Equal(LanguageServerIndicatorState.Off, controller.Indicator.State);
        Assert.Empty(Launches);

        controller.Toggle();
        await WaitForAsync(() => controller.Indicator.State == LanguageServerIndicatorState.Running);
        await _server.WaitForAsync("textDocument/didOpen");

        Assert.Contains((Workspace, "fake-server"), _enabled);
        var launch = Assert.Single(Launches);
        Assert.Equal(["--stdio", "app"], launch.Arguments);
        var open = Assert.Single(_server.ReceivedOf("textDocument/didOpen"))!["textDocument"]!;
        Assert.Equal(LanguageServerClient.ToUri(_filePath!), open["uri"]!.GetValue<string>());
        Assert.Equal("typescript", open["languageId"]!.GetValue<string>());
        Assert.Equal("let value = 1;", open["text"]!.GetValue<string>());
        Assert.Equal(["."], controller.CompletionTriggerCharacters);
    }

    [Fact]
    public async Task A_restart_opens_the_document_in_the_new_server()
    {
        await using var second = new FakeLanguageServer();
        var servers = new Queue<FakeLanguageServer>([_server, second]);
        _registry.Dispose();
        _registry = new LanguageServerRegistry(
            (_, _) => true,
            (_, _, _) => { },
            (launch, log) => servers.Dequeue().CreateClient(launch.WorkspaceFolder, log: log));
        using var controller = CreateController();
        controller.Update();
        await _server.WaitForAsync("textDocument/didOpen");

        controller.Restart();
        await second.WaitForAsync("textDocument/didOpen");
        await WaitForAsync(() => controller.Indicator.State == LanguageServerIndicatorState.Running);

        await _server.WaitForAsync("shutdown");
        var open = Assert.Single(second.ReceivedOf("textDocument/didOpen"))!["textDocument"]!;
        Assert.Equal("let value = 1;", open["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Problems_the_server_reports_become_the_documents_problems()
    {
        _enabled.Add((Workspace, "fake-server"));
        using var controller = CreateController();
        controller.Update();
        await _server.WaitForAsync("textDocument/didOpen");

        await _server.NotifyAsync("textDocument/publishDiagnostics", new JsonObject
        {
            ["uri"] = LanguageServerClient.ToUri(_filePath!),
            ["version"] = 1,
            ["diagnostics"] = JsonNode.Parse("""
                [{ "range": { "start": { "line": 0, "character": 4 }, "end": { "line": 0, "character": 9 } },
                   "severity": 2, "message": "Unused value." }]
                """)
        });
        await WaitForAsync(() => _diagnostics.Items.Count > 0);

        var problem = Assert.Single(_diagnostics.Items);
        Assert.Equal(new TextRange(4, 5), problem.Range);
        Assert.Equal(DiagnosticSeverity.Warning, problem.Severity);
        Assert.Equal("fake-server", problem.Source);

        controller.Toggle();

        Assert.Empty(_diagnostics.Items);
        Assert.Equal(LanguageServerIndicatorState.Off, controller.Indicator.State);
        await _server.WaitForAsync("shutdown");
    }

    [Fact]
    public async Task Completion_asks_the_server_for_the_text_the_editor_shows()
    {
        _enabled.Add((Workspace, "fake-server"));
        _server.Handlers["textDocument/completion"] = _ => new JsonArray(
            new JsonObject { ["label"] = "valueOf" },
            new JsonObject { ["label"] = "toString" });
        using var controller = CreateController();
        controller.Update();
        await WaitForAsync(() => controller.Indicator.State == LanguageServerIndicatorState.Running);
        var provider = controller.GetCompletionProvider(new DocumentWordCompletionProvider());

        var text = "let value = 1;\nvalue.va";
        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(new TextSnapshot(text), text.Length, TextSelection.Caret(text.Length)));

        Assert.Equal(["valueOf"], result!.Items.Select(item => item.Label));
        var change = Assert.Single(_server.ReceivedOf("textDocument/didChange"))!;
        Assert.Equal(text, change["contentChanges"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Without_a_running_server_completion_falls_back()
    {
        using var controller = CreateController();
        controller.Update();
        var provider = controller.GetCompletionProvider(new DocumentWordCompletionProvider());

        var text = "alpha alp";
        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(new TextSnapshot(text), text.Length, TextSelection.Caret(text.Length)));

        Assert.Equal(["alpha"], result!.Items.Select(item => item.Label));
    }

    private LanguageServerController CreateController() =>
        new(
            _editor,
            () => new DocumentSessionState(_filePath, TextEncoding.Utf8, LineEndingKind.Lf, IsDirty: false),
            () => "typescript",
            () => _definition,
            () => [],
            _registry,
            _diagnostics,
            new FakeUiDispatcher());

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
