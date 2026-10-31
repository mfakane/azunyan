using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class SnippetSessionTests
{
    [Fact]
    public void Tab_stops_are_selected_in_order_and_typing_changes_every_place_of_one()
    {
        var (document, session) = Insert("x = ", "for (${1:i} = 0; $1 < ${2:n}; $1++) {$0}");

        Select(document, session);
        Assert.Equal(["i", "i", "i"], document.CaretSet.Select(caret => document.Snapshot.GetText(caret.Selection.Range)));

        Type(document, session, "index");
        Assert.Equal("x = for (index = 0; index < n; index++) {}", document.Text);
        Assert.True(session.Contains(document.CaretSet));

        session.Move(1);
        Select(document, session);
        Assert.Equal("n", document.Snapshot.GetText(document.Selection.Range));
        Type(document, session, "count");

        session.Move(-1);
        Select(document, session);
        Assert.Equal(["index", "index", "index"], document.CaretSet.Select(caret => document.Snapshot.GetText(caret.Selection.Range)));

        session.Move(1);
        session.Move(1);
        Assert.True(session.IsFinished);
        Select(document, session);
        Assert.Equal(document.Text.IndexOf('}', StringComparison.Ordinal), document.Selection.CaretPosition);
    }

    [Fact]
    public void Typing_over_a_placeholder_drops_those_in_its_default()
    {
        var (document, session) = Insert(string.Empty, "f(${1:a, ${2:b}})");

        Select(document, session);
        Type(document, session, "z");
        session.Move(1);

        Assert.True(session.IsFinished);
        Select(document, session);
        Assert.Equal("f(z)".Length, document.Selection.CaretPosition);
    }

    [Fact]
    public void Without_a_final_tab_stop_the_end_of_the_snippet_is_one()
    {
        var (document, session) = Insert(string.Empty, "print($1)");

        Select(document, session);
        Type(document, session, "1");
        session.Move(1);
        Select(document, session);

        Assert.True(session.IsFinished);
        Assert.Equal("print(1)".Length, document.Selection.CaretPosition);
    }

    [Fact]
    public void Edits_around_the_snippet_move_it_and_others_end_it()
    {
        var (document, session) = Insert("ab", "f(${1:x})");
        Select(document, session);

        Assert.True(Follow(document, session, () => document.Insert(0, "--")));
        Assert.True(Follow(document, session, () => document.Insert(document.Length, "!")));
        Select(document, session);
        Assert.Equal("x", document.Snapshot.GetText(document.Selection.Range));

        Assert.False(Follow(document, session, () => document.Insert(document.Text.IndexOf('(', StringComparison.Ordinal), "g")));
        Assert.False(session.Contains(new TextCaretSet([new TextCaretState(TextSelection.Caret(0), 0)])));
    }

    private static (Document Document, SnippetSession Session) Insert(string before, string snippetText)
    {
        var snippet = Snippet.Parse(snippetText);
        var document = new Document(before + snippet.Text);
        return (document, SnippetSession.Start(before.Length, snippet.Text, snippet.Placeholders));
    }

    private static void Select(Document document, SnippetSession session) =>
        document.SetCaretSet(session.GetSelection(_ => 0));

    /// <summary>Replaces the selection of every caret, as typing does.</summary>
    private static void Type(Document document, SnippetSession session, string text)
    {
        var edit = TextCaretSetOperations.CreateReplacement(
            document.Snapshot,
            document.CaretSet,
            [.. document.CaretSet.Select(_ => text)],
            4);
        Assert.True(Follow(document, session, () => document.Replace(edit.Range, edit.Replacement, edit.CaretSet)));
    }

    private static bool Follow(Document document, SnippetSession session, Action edit)
    {
        var followed = true;
        void OnChanged(object? sender, DocumentChangedEventArgs args) =>
            followed &= session.Apply(args.Change, args.OldSnapshot, args.NewSnapshot);
        document.Changed += OnChanged;
        edit();
        document.Changed -= OnChanged;
        return followed;
    }
}
