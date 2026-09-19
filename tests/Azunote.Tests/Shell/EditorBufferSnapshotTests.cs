using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class EditorBufferSnapshotTests
{
    [Fact]
    public void Capture_clamps_native_positions_to_the_snapshot()
    {
        var editor = new StaleEditorBuffer(
            new TextSnapshot("first\nsecond"),
            new TextSelection(100, 100));

        var captured = EditorBufferSnapshot.Capture(editor);

        Assert.Equal(12, captured.Selection.CaretPosition);
        Assert.Equal(new LineColumn(1, 6), captured.Caret);
        Assert.Equal(new LineColumn(1, 6), captured.SelectionStart);
        Assert.Equal(new LineColumn(1, 6), captured.SelectionEnd);
        Assert.Empty(captured.SelectedText);
    }

    private sealed class StaleEditorBuffer(TextSnapshot snapshot, TextSelection selection)
        : IEditorBuffer
    {
        public string Text => snapshot.Text;

        public string SelectedText => string.Empty;

        public TextSnapshot Snapshot => snapshot;

        public TextSelection Selection => selection;

        public int CaretPosition => selection.CaretPosition;

        public event EventHandler<TextChange>? Edited
        {
            add { }
            remove { }
        }

        public void SetText(string text) => throw new NotSupportedException();

        public void SetSelection(TextSelection selection) => throw new NotSupportedException();

        public void Replace(TextRange range, string replacement) => throw new NotSupportedException();
    }
}
