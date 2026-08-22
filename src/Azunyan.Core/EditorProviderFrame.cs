namespace Azunyan.Core;

/// <summary>
/// The immutable channel snapshot consumed by a renderer. Each populated
/// result must belong to <see cref="Snapshot"/>; a frame may be partial while
/// the independent provider channels are completing.
/// </summary>
public sealed class EditorProviderFrame
{
    public EditorProviderFrame(
        TextSnapshot snapshot,
        TextSelection selection,
        DocumentProviderResults? document = null,
        ViewportProviderResults? viewport = null,
        PositionProviderResults? position = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(document?.Snapshot, snapshot, nameof(document));
        ValidateSnapshot(viewport?.Context.Snapshot, snapshot, nameof(viewport));
        ValidateSnapshot(position?.Context.Snapshot, snapshot, nameof(position));

        Snapshot = snapshot;
        Selection = selection;
        Document = document;
        Viewport = viewport;
        Position = position;
    }

    public TextSnapshot Snapshot { get; }

    public TextSelection Selection { get; }

    public DocumentProviderResults? Document { get; }

    public ViewportProviderResults? Viewport { get; }

    public PositionProviderResults? Position { get; }

    public int CaretPosition => Position?.Context.Position ?? Selection.CaretPosition;

    /// <summary>
    /// Produces the pre-channel aggregate shape for callers that have not yet
    /// migrated to channel-specific results. Missing channels are empty or
    /// null; the returned value is still bound to this frame's snapshot.
    /// </summary>
    public EditorProviderResults ToLegacyResults(long requestId = 0) =>
        new(
            requestId,
            new EditorProviderContext(Snapshot, CaretPosition, Selection),
            Document?.Syntax ?? Array.Empty<SyntaxSpan>(),
            Document?.Decorations ?? Array.Empty<TextDecoration>(),
            Position?.Tooltip,
            Position?.Completions,
            Viewport?.Gutter ?? Array.Empty<GutterItem>());

    private static void ValidateSnapshot(
        TextSnapshot? resultSnapshot,
        TextSnapshot snapshot,
        string parameterName)
    {
        if (resultSnapshot is not null && !ReferenceEquals(resultSnapshot, snapshot))
        {
            throw new ArgumentException(
                "All provider results in a frame must belong to the same snapshot.",
                parameterName);
        }
    }
}

public sealed class EditorProviderFrameEventArgs : EventArgs
{
    public EditorProviderFrameEventArgs(EditorProviderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Frame = frame;
    }

    public EditorProviderFrame Frame { get; }
}
