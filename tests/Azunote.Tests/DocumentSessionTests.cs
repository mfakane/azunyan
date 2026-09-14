using Azunyan.Core;
using Xunit;

namespace Azunote.Tests;

public sealed class DocumentSessionTests
{
    [Theory]
    [InlineData("a\r\nb\rc\n", "a\nb\nc\n", true)]
    [InlineData("a\nb\nc\n", "a\r\nb\rc\n", true)]
    [InlineData("a\r\n", "a\r", true)]
    [InlineData("a\r\n", "a", false)]
    [InlineData("a", "a\r\n", false)]
    [InlineData("a\r\r\n", "a\n", false)]
    [InlineData("", "\r", false)]
    public void Saved_comparison_preserves_mixed_newlines_and_trailing_newlines(string saved, string current, bool same)
    {
        var session = new DocumentSession();
        session.MarkSaved("test.txt", saved, TextEncodingKind.Utf8, LineEndingKind.CrLf);
        Assert.Equal(same, session.IsSameAsSaved(current));
        session.ObserveText(current);
        Assert.Equal(!same, session.State.IsDirty);
        session.ObserveText(saved);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void Comparing_large_CRLF_document_does_not_allocate_normalized_copies()
    {
        var saved = string.Concat(Enumerable.Repeat("line\r\n", 100_000));
        var session = new DocumentSession();
        session.MarkSaved("large.txt", saved, TextEncodingKind.Utf8, LineEndingKind.CrLf);
        session.IsSameAsSaved(saved); // warm up diagnostics
        var before = GC.GetAllocatedBytesForCurrentThread();
        var same = session.IsSameAsSaved(saved);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(same);
        Assert.True(allocated < 1024, $"Allocated {allocated} bytes comparing saved text");
    }

    [Fact]
    public void Untitled_text_is_dirty_only_when_it_differs_from_empty_saved_text()
    {
        var session = new DocumentSession();

        session.SetUntitled("", TextEncodingKind.Utf8, LineEndingKind.Lf);
        Assert.False(session.State.IsDirty);

        session.SetUntitled("from stdin", TextEncodingKind.Utf8, LineEndingKind.Lf);
        Assert.True(session.State.IsDirty);
        Assert.Null(session.State.FilePath);
    }

    [Fact]
    public void Observing_text_uses_saved_content_semantics()
    {
        var session = new DocumentSession();
        session.Load(
            "notes.txt",
            new TextFileData("before", TextEncodingKind.Utf8, LineEndingKind.Lf));

        session.ObserveText("after");
        Assert.True(session.State.IsDirty);

        session.ObserveText("before");
        Assert.False(session.State.IsDirty);
        Assert.True(session.IsSameAsSaved("before"));
        Assert.False(session.IsSameAsSaved("after"));
    }

    [Fact]
    public void Observing_text_ignores_line_ending_representation_changes()
    {
        var session = new DocumentSession();
        session.Load(
            "notes.txt",
            new TextFileData("before\r\nafter", TextEncodingKind.Utf8, LineEndingKind.CrLf));

        session.ObserveText("before\nafter");

        Assert.False(session.State.IsDirty);
        Assert.True(session.IsSameAsSaved("before\nafter"));
    }

    [Fact]
    public void Temporary_reload_preserves_saved_text_and_file_identity()
    {
        var session = new DocumentSession();
        session.Load(
            "notes.txt",
            new TextFileData("saved", TextEncodingKind.Utf8, LineEndingKind.Lf));

        session.ApplyTemporaryReload(
            new TextFileData("formatted", TextEncodingKind.Utf8Bom, LineEndingKind.CrLf),
            "formatted");

        Assert.Equal(Path.GetFullPath("notes.txt"), session.State.FilePath);
        Assert.Equal(TextEncodingKind.Utf8Bom, session.State.Encoding);
        Assert.Equal(LineEndingKind.CrLf, session.State.LineEnding);
        Assert.True(session.State.IsDirty);
        Assert.True(session.IsSameAsSaved("saved"));
    }

    [Fact]
    public void Mark_saved_updates_path_and_clears_dirty_state()
    {
        var session = new DocumentSession();
        session.SetUntitled("draft", TextEncodingKind.Utf8, LineEndingKind.Lf);

        session.MarkSaved(
            "saved.md",
            "draft",
            TextEncodingKind.Utf8Bom,
            LineEndingKind.CrLf);

        Assert.Equal(Path.GetFullPath("saved.md"), session.State.FilePath);
        Assert.Equal(TextEncodingKind.Utf8Bom, session.State.Encoding);
        Assert.Equal(LineEndingKind.CrLf, session.State.LineEnding);
        Assert.False(session.State.IsDirty);
    }
}
