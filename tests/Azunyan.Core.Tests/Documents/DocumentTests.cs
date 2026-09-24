using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class DocumentTests
{
    [Fact]
    public void Views_share_text_but_keep_selection_independent()
    {
        var first = new Document("alpha beta");
        var second = first.CreateView();
        first.SetCaret(5);
        second.SetSelection(new TextSelection(6, 10));

        first.Insert(5, "!");

        Assert.Equal("alpha! beta", second.Text);
        Assert.Equal(TextSelection.Caret(6), first.Selection);
        Assert.Equal(new TextSelection(7, 11), second.Selection);
    }

    [Fact]
    public void Undo_history_is_shared_between_views_without_sharing_carets()
    {
        var first = new Document("abc");
        var second = first.CreateView();
        second.SetCaret(0);
        first.SetCaret(3);
        first.Insert("d");

        Assert.True(second.Undo());

        Assert.Equal("abc", first.Text);
        Assert.Equal("abc", second.Text);
        Assert.Equal(3, first.CaretPosition);
        Assert.Equal(0, second.CaretPosition);
    }

    [Fact]
    public void Edits_return_coarse_changes_and_keep_snapshots_immutable()
    {
        var document = new Document("0123456789");
        var original = document.Snapshot;

        var change = document.Replace(new TextRange(2, 3), "abc");

        Assert.Equal("0123456789", original.Text);
        Assert.Equal("01abc56789", document.Text);
        Assert.Equal(new TextRange(2, 3), change.OldRange);
        Assert.Equal("234", change.OldText);
        Assert.Equal("abc", change.NewText);
        Assert.Equal(new TextRange(2, 3), change.NewRange);
        Assert.Equal("23456", original.GetText(new TextRange(2, 5)));
        Assert.Equal("abc56", document.Snapshot.GetText(new TextRange(2, 5)));
    }

    [Fact]
    public void Undo_and_redo_restore_text_and_selection()
    {
        var document = new Document("hello");
        document.SetSelection(new TextSelection(1, 4));

        document.Replace("i");

        Assert.Equal("hio", document.Text);
        Assert.Equal(TextSelection.Caret(2), document.Selection);
        Assert.True(document.Undo());
        Assert.Equal("hello", document.Text);
        Assert.Equal(new TextSelection(1, 4), document.Selection);
        Assert.True(document.Redo());
        Assert.Equal("hio", document.Text);
        Assert.Equal(TextSelection.Caret(2), document.Selection);
    }

    [Fact]
    public void Undo_group_merges_consecutive_insertions()
    {
        var document = new Document();

        document.BeginUndoGroup();
        document.Insert("a");
        document.Insert("b");
        document.Insert("c");
        document.EndUndoGroup();

        Assert.Equal("abc", document.Text);
        Assert.True(document.Undo());
        Assert.Equal(string.Empty, document.Text);
        Assert.Equal(TextSelection.Caret(0), document.Selection);
        Assert.False(document.CanUndo);
        Assert.True(document.Redo());
        Assert.Equal("abc", document.Text);
        Assert.Equal(TextSelection.Caret(3), document.Selection);
    }

    [Fact]
    public void Undo_group_stops_merging_after_selection_movement()
    {
        var document = new Document();

        document.BeginUndoGroup();
        document.Insert("a");
        document.SetCaret(0);
        document.Insert("b");
        document.EndUndoGroup();

        Assert.Equal("ba", document.Text);
        Assert.True(document.Undo());
        Assert.Equal("a", document.Text);
        Assert.True(document.Undo());
        Assert.Equal(string.Empty, document.Text);
    }

    [Fact]
    public void New_edit_clears_redo_history()
    {
        var document = new Document("a");
        document.Insert(1, "b");
        Assert.True(document.Undo());

        document.Insert(0, "c");

        Assert.False(document.CanRedo);
        Assert.Equal("ca", document.Text);
    }

    [Fact]
    public void Line_index_handles_all_newline_forms()
    {
        var snapshot = new TextSnapshot("a\r\nb\nc\rd");
        var lines = snapshot.Lines;

        Assert.Equal(4, lines.LineCount);
        Assert.Equal(new TextRange(0, 1), lines.GetLineRange(0));
        Assert.Equal(new TextRange(3, 1), lines.GetLineRange(1));
        Assert.Equal(new TextRange(5, 1), lines.GetLineRange(2));
        Assert.Equal(new TextRange(7, 1), lines.GetLineRange(3));
        Assert.Equal(new LineColumn(2, 1), lines.GetLineColumn(6));
        Assert.Equal(6, lines.GetPosition(new LineColumn(2, 1)));
    }

    [Fact]
    public void Line_index_handles_crlf_split_across_persistent_text_pieces()
    {
        var document = new Document("a\r");
        document.Insert(2, "\n");

        var lines = document.Snapshot.Lines;

        Assert.Equal(2, lines.LineCount);
        Assert.Equal(new TextRange(0, 1), lines.GetLineRange(0));
        Assert.Equal(new TextRange(3, 0), lines.GetLineRange(1));
    }

    [Fact]
    public void Search_supports_non_overlapping_and_overlapping_matches()
    {
        var snapshot = new TextSnapshot("ababa");

        Assert.Equal(new TextRange(0, 3), snapshot.Find("aba"));
        Assert.Equal(
            new[] { new TextRange(0, 3) },
            snapshot.FindAll("aba"));
        Assert.Equal(
            new[] { new TextRange(0, 3), new TextRange(2, 3) },
            snapshot.FindAll("aba", allowOverlapping: true));
    }

    [Fact]
    public void Persistent_text_tree_survives_many_random_edits()
    {
        var random = new Random(42);
        var expected = string.Empty;
        var document = new Document();

        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var start = random.Next(expected.Length + 1);
            var length = random.Next(expected.Length - start + 1);
            var inserted = new string((char)('a' + random.Next(3)), random.Next(4));

            document.Replace(new TextRange(start, length), inserted);
            expected = expected.Remove(start, length).Insert(start, inserted);

            Assert.Equal(expected, document.Text);

            var rangeStart = random.Next(expected.Length + 1);
            var rangeLength = random.Next(expected.Length - rangeStart + 1);
            Assert.Equal(
                expected.Substring(rangeStart, rangeLength),
                document.Snapshot.GetText(new TextRange(rangeStart, rangeLength)));
        }
    }

    [Fact]
    public void Scalar_navigation_does_not_split_surrogate_pairs()
    {
        const string text = "A😀B";

        Assert.Equal(3, UnicodeText.GetNextScalarPosition(text, 1));
        Assert.Equal(1, UnicodeText.GetPreviousScalarPosition(text, 3));
        Assert.Equal(3, UnicodeText.MoveByScalars(text, 0, 2));

        var document = new Document(text);
        document.SetCaret(3);
        document.DeleteBackwardByScalar();
        Assert.Equal("AB", document.Text);
    }

    [Fact]
    public void Word_range_uses_unicode_text_elements_and_skips_whitespace()
    {
        const string text = "one 日本語 é +++ two";

        Assert.Equal(new TextRange(0, 3), UnicodeText.GetWordRange(text, 1));
        Assert.Equal(new TextRange(4, 3), UnicodeText.GetWordRange(text, 5));
        Assert.Equal(new TextRange(8, 2), UnicodeText.GetWordRange(text, 9));
        Assert.Equal(new TextRange(11, 3), UnicodeText.GetWordRange(text, 12));
        Assert.Equal(TextRange.Empty(3), UnicodeText.GetWordRange(text, 3));
    }

    [Fact]
    public void Current_line_range_excludes_the_line_ending()
    {
        var snapshot = new TextSnapshot("first\r\nsecond\n");

        Assert.Equal(new TextRange(0, 5), TextEditorCommands.GetCurrentLineRange(snapshot, 2));
        Assert.Equal(new TextRange(7, 6), TextEditorCommands.GetCurrentLineRange(snapshot, 9));
        Assert.Equal(TextRange.Empty(snapshot.Length),
            TextEditorCommands.GetCurrentLineRange(snapshot, snapshot.Length));

        Assert.Equal(new TextRange(0, 7),
            TextEditorCommands.GetCurrentLineDeletionRange(snapshot, 2));
        Assert.Equal(new TextRange(7, 7),
            TextEditorCommands.GetCurrentLineDeletionRange(snapshot, 9));
    }

    [Fact]
    public void Newline_auto_indent_carries_leading_spaces_and_preserves_line_ending()
    {
        var document = new Document("  first\r\nsecond");
        document.SetCaret(7);

        var change = TextEditorCommands.InsertNewLineWithAutoIndent(document);

        Assert.Equal("  first\r\n  \r\nsecond", document.Text);
        Assert.Equal(new TextRange(7, 0), change.OldRange);
        Assert.Equal("\r\n  ", change.NewText);
        Assert.Equal(11, document.CaretPosition);
        Assert.Equal("\r\n  ", TextEditorCommands.GetNewLineWithAutoIndentation(
            new TextSnapshot("  first\r\nsecond"),
            7));
    }

    [Fact]
    public void Newline_auto_indent_carries_tabs_and_replaces_selection()
    {
        var document = new Document("\tfirst\nsecond");
        document.SetSelection(new TextSelection(2, 6));

        TextEditorCommands.InsertNewLineWithAutoIndent(document);

        Assert.Equal("\tf\n\t\nsecond", document.Text);
        Assert.Equal(4, document.CaretPosition);
    }

    [Fact]
    public void Newline_auto_indent_uses_platform_line_ending_for_a_new_document()
    {
        var document = new Document("text");
        document.SetCaret(document.Length);

        TextEditorCommands.InsertNewLineWithAutoIndent(document);

        Assert.Equal("text" + Environment.NewLine, document.Text);
    }

    [Fact]
    public void Newline_auto_indent_adds_a_level_after_json_delimiters()
    {
        var document = new Document("{");
        document.SetCaret(document.Length);

        TextEditorCommands.InsertNewLineWithAutoIndent(document);

        Assert.Equal("{" + Environment.NewLine + "  ", document.Text);
        Assert.Equal(Environment.NewLine + "  ",
            TextEditorCommands.GetNewLineWithAutoIndentation(
                new TextSnapshot("{"),
                1));

        document.Insert("\"name\": [");
        TextEditorCommands.InsertNewLineWithAutoIndent(document);

        Assert.Equal(
            "{" + Environment.NewLine + "  \"name\": [" + Environment.NewLine + "    ",
            document.Text);
    }

    [Fact]
    public void Newline_auto_indent_dedents_closing_json_delimiters()
    {
        var snapshot = new TextSnapshot("{" + Environment.NewLine + "  }");
        var position = snapshot.Length;

        Assert.Equal(Environment.NewLine,
            TextEditorCommands.GetNewLineWithAutoIndentation(snapshot, position));

        var indentationSnapshot = new TextSnapshot("{" + Environment.NewLine + "  ");
        Assert.True(TextEditorCommands.TryGetClosingDelimiterDedent(
            indentationSnapshot,
            indentationSnapshot.Length,
            '}',
            out var indentationRange));
        Assert.Equal(new TextRange(indentationSnapshot.Length - 2, 2), indentationRange);

        var nestedIndentationSnapshot = new TextSnapshot(
            "{" + Environment.NewLine + "  a: [" + Environment.NewLine + "    \"b\"" + Environment.NewLine + "    ");
        Assert.True(TextEditorCommands.TryGetClosingDelimiterDedent(
            nestedIndentationSnapshot,
            nestedIndentationSnapshot.Length,
            ']',
            out var nestedIndentationRange,
            out var targetIndentation));
        Assert.Equal(new TextRange(nestedIndentationSnapshot.Length - 4, 4), nestedIndentationRange);
        Assert.Equal("  ", targetIndentation);
    }

    [Fact]
    public void Newline_auto_indent_keeps_a_following_closing_delimiter_on_its_line()
    {
        var newline = Environment.NewLine;
        var document = new Document("{" + newline + "  a: [" + newline + "}");
        document.SetCaret(("{" + newline + "  a: [").Length);

        TextEditorCommands.InsertNewLineWithAutoIndent(document);

        Assert.Equal("{" + newline + "  a: [" + newline + "    " + newline + "}", document.Text);
    }

    [Fact]
    public void Newline_auto_indent_preserves_a_manual_dedent_on_a_regular_line()
    {
        var snapshot = new TextSnapshot("{\n  a\nb");

        Assert.Equal("\n", TextEditorCommands.GetNewLineWithAutoIndentation(
            snapshot,
            snapshot.Length));
    }

    [Fact]
    public void Newline_auto_indent_keeps_the_previous_line_indentation_through_blank_lines()
    {
        var lineWithText = new TextSnapshot("  a\n  b");
        var blankLine = new TextSnapshot("  a\n  b\n  ");

        Assert.Equal("\n  ", TextEditorCommands.GetNewLineWithAutoIndentation(
            lineWithText,
            lineWithText.Length));
        Assert.Equal("\n  ", TextEditorCommands.GetNewLineWithAutoIndentation(
            blankLine,
            blankLine.Length));
    }

    [Fact]
    public void Newline_auto_indent_turns_an_indented_blank_line_into_a_true_blank_line()
    {
        var document = new Document("  a\n  b\n  ");
        document.SetCaret(document.Length);

        TextEditorCommands.InsertNewLineWithAutoIndent(document);

        Assert.Equal("  a\n  b\n\n  ", document.Text);
    }

    [Fact]
    public void Tab_inserts_at_the_caret_without_a_selection()
    {
        var document = new Document("ab");
        document.SetCaret(1);

        TextEditorCommands.IndentSelection(document);

        Assert.Equal("a\tb", document.Text);
        Assert.Equal(2, document.CaretPosition);
    }

    [Fact]
    public void Tab_indents_every_line_touched_by_a_selection()
    {
        var document = new Document("a\nb\nc");
        document.SetSelection(new TextSelection(0, 3));

        TextEditorCommands.IndentSelection(document);

        Assert.Equal("\ta\n\tb\nc", document.Text);
        Assert.Equal(new TextSelection(1, 5), document.Selection);
    }

    [Fact]
    public void Shift_tab_dedents_every_line_touched_by_a_selection()
    {
        var document = new Document("  a\n  b\nc");
        document.SetSelection(new TextSelection(0, 7));

        TextEditorCommands.IndentSelection(document, dedent: true);

        Assert.Equal("a\nb\nc", document.Text);
        Assert.Equal(new TextSelection(0, 3), document.Selection);
    }

    [Fact]
    public void Shift_tab_dedents_a_single_line_without_a_selection()
    {
        var document = new Document("  abc");
        document.SetCaret(document.Length);

        TextEditorCommands.IndentSelection(document, dedent: true);

        Assert.Equal("abc", document.Text);
        Assert.Equal(3, document.CaretPosition);
    }

    [Fact]
    public void Indent_size_controls_space_indentation()
    {
        var document = new Document("abc");
        document.SetCaret(0);

        TextEditorCommands.IndentSelection(document, indentSize: 8);

        Assert.Equal("        abc", document.Text);
        Assert.Equal(8, document.CaretPosition);
    }

    [Fact]
    public void Tab_input_mode_inserts_a_literal_tab()
    {
        var document = new Document("abc");
        document.SetCaret(0);

        TextEditorCommands.IndentSelection(
            document,
            inputMode: IndentationInputMode.Tab);

        Assert.Equal("\tabc", document.Text);
        Assert.Equal(1, document.CaretPosition);
    }

    [Fact]
    public void Spaces_input_mode_inserts_configured_spaces()
    {
        var document = new Document("\tabc");
        document.SetCaret(0);

        TextEditorCommands.IndentSelection(
            document,
            indentSize: 4,
            inputMode: IndentationInputMode.Spaces);

        Assert.Equal("    \tabc", document.Text);
        Assert.Equal(4, document.CaretPosition);
    }

    [Fact]
    public void Tab_input_mode_shift_tab_removes_a_space_indent_unit()
    {
        var document = new Document("    abc");
        document.SetCaret(document.Length);

        TextEditorCommands.IndentSelection(
            document,
            dedent: true,
            indentSize: 4,
            inputMode: IndentationInputMode.Tab);

        Assert.Equal("abc", document.Text);
        Assert.Equal(3, document.CaretPosition);
    }

    [Fact]
    public void Auto_indent_size_infers_the_document_space_indentation()
    {
        var document = new Document("    parent\n        child");
        document.SetCaret(0);

        TextEditorCommands.IndentSelection(document);

        Assert.Equal("        parent\n        child", document.Text);
        Assert.Equal(4, document.CaretPosition);
    }

    [Fact]
    public void Shift_tab_removes_up_to_the_configured_space_indentation_size()
    {
        var document = new Document("        abc");
        document.SetCaret(document.Length);

        TextEditorCommands.IndentSelection(document, dedent: true, indentSize: 4);

        Assert.Equal("    abc", document.Text);
        Assert.Equal(7, document.CaretPosition);
    }

    [Fact]
    public void Newline_auto_indent_ignores_delimiters_in_strings_and_comments()
    {
        var snapshot = new TextSnapshot(
            "{\n  \"literal\": \"}\", // ]\n  value");

        Assert.Equal("\n  ",
            TextEditorCommands.GetNewLineWithAutoIndentation(snapshot, snapshot.Length));
    }

    [Fact]
    public void Newline_auto_indent_uses_an_explicit_line_ending_when_configured()
    {
        var snapshot = new TextSnapshot("{\n  value");

        Assert.Equal(
            "\r\n  ",
            TextEditorCommands.GetNewLineWithAutoIndentation(
                snapshot,
                snapshot.Length,
                "\r\n"));

        var document = new Document("value");
        document.SetCaret(document.Length);
        TextEditorCommands.InsertNewLineWithAutoIndent(document, "\r");

        Assert.Equal("value\r", document.Text);
    }

    [Fact]
    public void Indentation_settings_infer_spaces_and_tabs()
    {
        Assert.Equal(
            new IndentationSettings(IndentationKind.Spaces, 2),
            TextEditorCommands.GetIndentationSettings(new TextSnapshot("{\n  value"), 8));
        Assert.Equal(
            new IndentationSettings(IndentationKind.Tabs, 4),
            TextEditorCommands.GetIndentationSettings(new TextSnapshot("value"), 0));
        Assert.Equal(
            new IndentationSettings(IndentationKind.Tabs, 4),
            TextEditorCommands.GetIndentationSettings(new TextSnapshot("{\n\tvalue"), 7));
        Assert.Equal(
            new IndentationSettings(IndentationKind.Spaces, 8),
            TextEditorCommands.GetIndentationSettings(
                new TextSnapshot("{\n  value"),
                8,
                indentSize: 8));
        Assert.Equal(
            new IndentationSettings(IndentationKind.Tabs, 8),
            TextEditorCommands.GetIndentationSettings(
                new TextSnapshot("{\n\tvalue"),
                7,
                tabDisplaySize: 8));
    }

    [Fact]
    public void Document_indentation_settings_infer_from_lines_after_the_caret()
    {
        Assert.Equal(
            new IndentationSettings(IndentationKind.Spaces, 2),
            TextEditorCommands.GetDocumentIndentationSettings(
                new TextSnapshot("value\n  child")));
        Assert.Equal(
            new IndentationSettings(IndentationKind.Tabs, 8),
            TextEditorCommands.GetDocumentIndentationSettings(
                new TextSnapshot("value\n\tchild"),
                tabDisplaySize: 8));
    }

    [Fact]
    public void Grapheme_navigation_and_delete_keep_emoji_and_combining_sequences_together()
    {
        const string text = "a👩‍💻éb";
        var emojiStart = 1;
        var emojiEnd = UnicodeText.GetNextTextElementPosition(text, emojiStart);
        var combiningStart = emojiEnd;
        var combiningEnd = UnicodeText.GetNextTextElementPosition(text, combiningStart);

        Assert.Equal("👩‍💻", text[emojiStart..emojiEnd]);
        Assert.Equal("é", text[combiningStart..combiningEnd]);

        var document = new Document(text);
        document.SetCaret(emojiEnd);
        document.DeleteBackward();
        Assert.Equal("aéb", document.Text);

        document.SetCaret(1);
        document.DeleteForward();
        Assert.Equal("ab", document.Text);
    }

    [Fact]
    public void Grapheme_movement_preserves_selection_anchor()
    {
        var document = new Document("a👩‍💻b");
        document.SetCaret(document.Length);
        document.MoveCaretByGrapheme(-2, extendSelection: true);

        Assert.Equal(new TextSelection(document.Length, 1), document.Selection);
    }

    [Fact]
    public void Matching_bracket_navigation_handles_nesting_from_either_bracket()
    {
        const string text = "call({ value: [1, (2)] })";
        var snapshot = new TextSnapshot(text);
        var opening = text.IndexOf('[', StringComparison.Ordinal);
        var closing = text.IndexOf(']', StringComparison.Ordinal);

        Assert.True(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            opening,
            out var matchingClosing));
        Assert.Equal(closing, matchingClosing);

        Assert.True(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            closing + 1,
            out var matchingOpening));
        Assert.Equal(opening, matchingOpening);
    }

    [Fact]
    public void Matching_bracket_navigation_uses_the_innermost_pair_enclosing_the_caret()
    {
        const string text = "# Configure the status bar's command.\n[terminal]\ncall({ value: [1] })";
        var snapshot = new TextSnapshot(text);
        var sectionCaret = text.IndexOf("terminal", StringComparison.Ordinal) + 3;
        var sectionClosing = text.IndexOf(']', StringComparison.Ordinal);
        var valueCaret = text.IndexOf("value", StringComparison.Ordinal) + 2;
        var objectClosing = text.IndexOf('}', StringComparison.Ordinal);

        Assert.True(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            sectionCaret,
            out var matchingSectionClosing));
        Assert.Equal(sectionClosing, matchingSectionClosing);

        Assert.True(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            valueCaret,
            out var matchingObjectClosing));
        Assert.Equal(objectClosing, matchingObjectClosing);
    }

    [Fact]
    public void Matching_bracket_navigation_scans_raw_text_without_language_semantics()
    {
        const string text = "{ \"}\": 1 } # [commented]";
        var snapshot = new TextSnapshot(text);
        var quotedClosing = text.IndexOf('}');
        var commentedOpening = text.IndexOf('[', StringComparison.Ordinal);
        var commentedClosing = text.IndexOf(']', StringComparison.Ordinal);

        Assert.True(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            0,
            out var matchingQuotedClosing));
        Assert.Equal(quotedClosing, matchingQuotedClosing);
        Assert.True(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            commentedOpening,
            out var matchingCommentedClosing));
        Assert.Equal(commentedClosing, matchingCommentedClosing);
    }

    [Fact]
    public void Matching_bracket_navigation_supports_angle_brackets()
    {
        const string text = "<node>";
        var snapshot = new TextSnapshot(text);

        Assert.True(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            0,
            out var matchingClosing));
        Assert.Equal(5, matchingClosing);
        Assert.True(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            snapshot.Length,
            out var matchingOpening));
        Assert.Equal(0, matchingOpening);
    }

    [Fact]
    public void Matching_bracket_navigation_is_a_no_op_without_a_pair()
    {
        var snapshot = new TextSnapshot("text (");

        Assert.False(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            snapshot.Length,
            out var matchingPosition));
        Assert.Equal(-1, matchingPosition);
        Assert.False(TextEditorCommands.TryFindMatchingBracket(
            snapshot,
            2,
            out _));
    }

    [Fact]
    public async Task Provider_coordinator_runs_all_providers_for_one_snapshot_and_position()
    {
        var providers = new EditorProviderSet
        {
            Syntax = new DelegateSyntaxProvider(context =>
                new[] { new SyntaxSpan(new TextRange(0, context.Snapshot.Length), "test") }),
            Decorations = new DelegateDecorationProvider(_ =>
                new[] { new TextDecoration(new TextRange(0, 1), "underline") }),
            Tooltip = new DelegateTooltipProvider(_ =>
                new TooltipData(new TextRange(0, 1), "tooltip")),
            Completion = new DelegateCompletionProvider(_ =>
                new CompletionResult(TextRange.Empty(0), new[] { new CompletionItem("item") })),
            Gutter = new DelegateGutterProvider(_ => new[] { new GutterItem(0, "!") })
        };
        using var coordinator = new EditorProviderCoordinator(providers);
        var snapshot = new TextSnapshot("hello");

        var results = await coordinator.RequestAsync(snapshot, 2, TextSelection.Caret(2));

        Assert.NotNull(results);
        Assert.Same(snapshot, results!.Context.Snapshot);
        Assert.Equal(2, results.Context.Position);
        Assert.Equal("test", Assert.Single(results.Syntax).Classification);
        Assert.Equal("underline", Assert.Single(results.Decorations).Kind);
        Assert.Equal("tooltip", results.Tooltip!.Content);
        Assert.Equal("item", Assert.Single(results.Completions!.Items).Label);
        Assert.Equal("!", Assert.Single(results.Gutter).Text);
    }

    [Fact]
    public async Task Provider_coordinator_discards_a_slow_result_when_a_new_request_arrives()
    {
        var oldRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providers = new EditorProviderSet
        {
            Syntax = new DelegateSyntaxProvider(async context =>
            {
                if (context.Snapshot.Text == "old")
                {
                    oldRequestStarted.SetResult();
                    await releaseOldRequest.Task;
                }

                return new[] { new SyntaxSpan(TextRange.Empty(0), context.Snapshot.Text) };
            })
        };
        using var coordinator = new EditorProviderCoordinator(providers);

        var oldTask = coordinator.RequestAsync(new TextSnapshot("old"), 0);
        await oldRequestStarted.Task;

        var newTask = coordinator.RequestAsync(new TextSnapshot("new"), 0);
        var newResults = await newTask;
        releaseOldRequest.SetResult();
        var oldResults = await oldTask;

        Assert.NotNull(newResults);
        Assert.Equal("new", Assert.Single(newResults!.Syntax).Classification);
        Assert.Null(oldResults);
    }

    private sealed class DelegateSyntaxProvider : ISyntaxProvider
    {
        private readonly Func<EditorProviderContext, Task<IReadOnlyList<SyntaxSpan>>> _handler;

        public DelegateSyntaxProvider(Func<EditorProviderContext, IEnumerable<SyntaxSpan>> handler)
        {
            _handler = context => Task.FromResult<IReadOnlyList<SyntaxSpan>>(handler(context).ToArray());
        }

        public DelegateSyntaxProvider(Func<EditorProviderContext, Task<IEnumerable<SyntaxSpan>>> handler)
        {
            _handler = async context => (await handler(context)).ToArray();
        }

        public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) => new(_handler(context));
    }

    private sealed class DelegateDecorationProvider : IDecorationProvider
    {
        private readonly Func<EditorProviderContext, IReadOnlyList<TextDecoration>> _handler;

        public DelegateDecorationProvider(Func<EditorProviderContext, IEnumerable<TextDecoration>> handler) =>
            _handler = context => handler(context).ToArray();

        public ValueTask<IReadOnlyList<TextDecoration>> GetDecorationsAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(context));
    }

    private sealed class DelegateTooltipProvider : ITooltipProvider
    {
        private readonly Func<EditorProviderContext, TooltipData?> _handler;

        public DelegateTooltipProvider(Func<EditorProviderContext, TooltipData?> handler) => _handler = handler;

        public ValueTask<TooltipData?> GetTooltipAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(context));
    }

    private sealed class DelegateCompletionProvider : ICompletionProvider
    {
        private readonly Func<EditorProviderContext, CompletionResult?> _handler;

        public DelegateCompletionProvider(Func<EditorProviderContext, CompletionResult?> handler) => _handler = handler;

        public ValueTask<CompletionResult?> GetCompletionsAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(context));
    }

    private sealed class DelegateGutterProvider : IGutterProvider
    {
        private readonly Func<EditorProviderContext, IReadOnlyList<GutterItem>> _handler;

        public DelegateGutterProvider(Func<EditorProviderContext, IEnumerable<GutterItem>> handler) =>
            _handler = context => handler(context).ToArray();

        public ValueTask<IReadOnlyList<GutterItem>> GetGutterItemsAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(context));
    }
}
