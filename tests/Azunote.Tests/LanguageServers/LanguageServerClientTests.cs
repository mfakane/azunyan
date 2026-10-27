using System.Text.Json.Nodes;
using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.LanguageServers;

public sealed class LanguageServerClientTests
{
    private static readonly string Workspace = Path.GetFullPath("workspace");
    private static readonly string DocumentUri = LanguageServerClient.ToUri(Path.Combine(Workspace, "app.ts"));

    [Fact]
    public async Task Initialize_sends_the_workspace_and_reads_the_capabilities()
    {
        await using var server = new FakeLanguageServer();
        var settings = new JsonObject { ["typescript"] = new JsonObject { ["format"] = true } };
        await using var client = server.CreateClient(Workspace, settings);

        await client.InitializeAsync(new JsonObject { ["option"] = 1 }, CancellationToken.None);
        await server.WaitForAsync("workspace/didChangeConfiguration");

        var initialize = Assert.Single(server.ReceivedOf("initialize"))!;
        Assert.Equal(LanguageServerClient.ToUri(Workspace), initialize["rootUri"]!.GetValue<string>());
        Assert.Equal(1, initialize["initializationOptions"]!["option"]!.GetValue<int>());
        Assert.Single(server.ReceivedOf("initialized"));
        Assert.True(client.SupportsCompletion);
        Assert.True(client.SupportsHover);
        Assert.Equal(["."], client.CompletionTriggerCharacters);
        Assert.True(
            Assert.Single(server.ReceivedOf("workspace/didChangeConfiguration"))!["settings"]!["typescript"]!["format"]!
                .GetValue<bool>());
    }

    [Fact]
    public async Task Without_a_workspace_folder_initialize_sends_no_root()
    {
        await using var server = new FakeLanguageServer();
        await using var client = server.CreateClient(string.Empty);

        await client.InitializeAsync(null, CancellationToken.None);
        var folders = await server.RequestAsync("workspace/workspaceFolders", null);

        var initialize = Assert.Single(server.ReceivedOf("initialize"))!;
        Assert.Null(initialize["rootUri"]);
        Assert.Null(initialize["rootPath"]);
        Assert.Null(initialize["workspaceFolders"]);
        Assert.Null(folders);
    }

    [Fact]
    public async Task A_document_opened_twice_is_opened_and_closed_once()
    {
        await using var server = new FakeLanguageServer();
        await using var client = server.CreateClient(Workspace);
        await client.InitializeAsync(null, CancellationToken.None);

        await client.OpenAsync(DocumentUri, "typescript", "let a = 1;");
        await client.OpenAsync(DocumentUri, "typescript", "let a = 1;");
        await client.ChangeAsync(DocumentUri, "let a = 1;");
        await client.ChangeAsync(DocumentUri, "let a = 2;");
        await client.CloseAsync(DocumentUri);
        await client.SaveAsync(DocumentUri, "let a = 2;");
        await client.CloseAsync(DocumentUri);
        await server.WaitForAsync("textDocument/didClose");

        var open = Assert.Single(server.ReceivedOf("textDocument/didOpen"))!;
        Assert.Equal("typescript", open["textDocument"]!["languageId"]!.GetValue<string>());
        var change = Assert.Single(server.ReceivedOf("textDocument/didChange"))!;
        Assert.Equal(2, change["textDocument"]!["version"]!.GetValue<int>());
        Assert.Equal("let a = 2;", change["contentChanges"]![0]!["text"]!.GetValue<string>());
        var save = Assert.Single(server.ReceivedOf("textDocument/didSave"))!;
        Assert.Equal("let a = 2;", save["text"]!.GetValue<string>());
        Assert.Single(server.ReceivedOf("textDocument/didClose"));
    }

    [Fact]
    public async Task Reopening_a_document_opens_it_anew_unless_another_window_has_it()
    {
        await using var server = new FakeLanguageServer();
        await using var client = server.CreateClient(Workspace);
        await client.InitializeAsync(null, CancellationToken.None);
        await client.OpenAsync(DocumentUri, "typescript", "one");
        await client.ChangeAsync(DocumentUri, "two");

        await client.ReopenAsync(DocumentUri, "typescript", "two");
        await client.OpenAsync(DocumentUri, "typescript", "two");
        await client.ReopenAsync(DocumentUri, "typescript", "three");
        await server.WaitForAsync("textDocument/didChange", 2);

        Assert.Single(server.ReceivedOf("textDocument/didClose"));
        var opens = server.ReceivedOf("textDocument/didOpen").ToArray();
        Assert.Equal(2, opens.Length);
        Assert.Equal(1, opens[1]!["textDocument"]!["version"]!.GetValue<int>());
        Assert.Equal("two", opens[1]!["textDocument"]!["text"]!.GetValue<string>());
        var change = server.ReceivedOf("textDocument/didChange").Last()!;
        Assert.Equal(2, change["textDocument"]!["version"]!.GetValue<int>());
        Assert.Equal("three", change["contentChanges"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_server_reads_its_settings_by_section()
    {
        await using var server = new FakeLanguageServer();
        var settings = new JsonObject
        {
            ["python"] = new JsonObject { ["analysis"] = new JsonObject { ["typeCheckingMode"] = "basic" } }
        };
        await using var client = server.CreateClient(Workspace, settings);
        await client.InitializeAsync(null, CancellationToken.None);

        var result = await server.RequestAsync("workspace/configuration", new JsonObject
        {
            ["items"] = new JsonArray(
                new JsonObject { ["section"] = "python.analysis" },
                new JsonObject { ["section"] = "missing" })
        });

        var values = Assert.IsType<JsonArray>(result);
        Assert.Equal("basic", values[0]!["typeCheckingMode"]!.GetValue<string>());
        Assert.Null(values[1]);
    }

    [Fact]
    public async Task Problems_for_an_older_version_are_dropped()
    {
        await using var server = new FakeLanguageServer();
        await using var client = server.CreateClient(Workspace);
        await client.InitializeAsync(null, CancellationToken.None);
        var published = new List<LanguageServerDiagnostics>();
        client.DiagnosticsPublished += diagnostics =>
        {
            lock (published)
            {
                published.Add(diagnostics);
            }
        };
        await client.OpenAsync(DocumentUri, "typescript", "one");
        await client.ChangeAsync(DocumentUri, "two");

        await server.NotifyAsync("textDocument/publishDiagnostics", Diagnostics(version: 1, "old"));
        // The server may write the drive letter in lower case and escape the colon.
        var serverUri = DocumentUri.Replace(":", "%3A", StringComparison.Ordinal).Replace("file%3A", "file:", StringComparison.Ordinal);
        var current = Diagnostics(version: 2, "new");
        current["uri"] = serverUri.ToLowerInvariant();
        await server.NotifyAsync("textDocument/publishDiagnostics", current);
        await WaitUntilAsync(() => published.Count > 0);

        var diagnostics = Assert.Single(published);
        Assert.Equal(DocumentUri, diagnostics.Uri);
        Assert.Equal("two", diagnostics.Text);
        Assert.Equal("new", diagnostics.Diagnostics[0]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Requests_ask_for_the_position_of_the_document()
    {
        await using var server = new FakeLanguageServer();
        server.Handlers["textDocument/completion"] = parameters => new JsonArray(new JsonObject
        {
            ["label"] = parameters!["position"]!["line"]!.GetValue<int>() + ":" + parameters["position"]!["character"]!.GetValue<int>()
        });
        await using var client = server.CreateClient(Workspace);
        await client.InitializeAsync(null, CancellationToken.None);

        var result = await client.CompletionAsync(DocumentUri, new LineColumn(2, 5), CancellationToken.None);

        Assert.Equal("2:5", result![0]!["label"]!.GetValue<string>());
    }

    [Fact]
    public async Task Disposing_asks_the_server_to_shut_down_and_exit()
    {
        await using var server = new FakeLanguageServer();
        var client = server.CreateClient(Workspace);
        await client.InitializeAsync(null, CancellationToken.None);

        await client.DisposeAsync();

        Assert.Single(server.ReceivedOf("shutdown"));
        await server.WaitForAsync("exit");
    }

    [Fact]
    public async Task A_server_that_closes_its_output_completes_the_client()
    {
        var server = new FakeLanguageServer();
        await using var client = server.CreateClient(Workspace);
        await client.InitializeAsync(null, CancellationToken.None);

        server.Close();

        await client.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<IOException>(
            () => client.CompletionAsync(DocumentUri, new LineColumn(0, 0), CancellationToken.None));
        await server.DisposeAsync();
    }

    [Theory]
    [InlineData("file:///C:/src/app.ts")]
    [InlineData("file:///c%3A/src/app.ts")]
    public void File_uris_read_as_the_same_path(string uri)
    {
        Assert.Equal(@"C:\src\app.ts", LanguageServerClient.ToPath(uri), ignoreCase: true);
    }

    private static JsonObject Diagnostics(int version, string message) => new()
    {
        ["uri"] = DocumentUri,
        ["version"] = version,
        ["diagnostics"] = new JsonArray(new JsonObject
        {
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
                ["end"] = new JsonObject { ["line"] = 0, ["character"] = 1 }
            },
            ["message"] = message
        })
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }

        // Anything published after the awaited message has had time to arrive.
        await Task.Delay(50);
    }
}
