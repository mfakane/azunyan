using Azunyan.Core;

namespace Azunyan.Syntax;

public sealed class XmlFoldingProvider : IFoldingAnalysisProvider
{
    public async ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
        => (await GetFoldingAnalysisAsync(context, cancellationToken).ConfigureAwait(false)).Folds;

    public ValueTask<FoldingAnalysis> GetFoldingAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.Snapshot;
        var frames = new List<XmlFrame>();
        var folds = new List<FoldRange>();
        var scan = XmlScanner.Scan(snapshot.Text, cancellationToken);
        var isComplete = scan.IsComplete;
        foreach (var token in scan.Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Kind == XmlTokenKind.StartElement)
            {
                if (!token.IsClosed || token.IsSelfClosing) continue;
                var parent = frames.Count == 0 ? null : frames[^1];
                var ordinal = parent?.NextOrdinal(token.Name) ?? 0;
                var path = parent is null ? $"$/{token.Name}[{ordinal}]" : $"{parent.Path}/{token.Name}[{ordinal}]";
                frames.Add(new XmlFrame(token, path));
            }
            else if (token.Kind == XmlTokenKind.EndElement && token.IsClosed && frames.Count > 0)
            {
                var frame = frames[^1];
                if (!string.Equals(frame.Name, token.Name, StringComparison.Ordinal))
                {
                    isComplete = false;
                    frames.Clear();
                    continue;
                }
                frames.RemoveAt(frames.Count - 1);
                if (snapshot.Lines.GetLine(frame.Token.Start) == snapshot.Lines.GetLine(token.Start)
                    || string.IsNullOrWhiteSpace(snapshot.GetText(TextRange.FromBounds(frame.Token.End, token.Start))))
                {
                    continue;
                }

                var line = snapshot.Lines.GetLine(token.Start);
                var lineEnd = snapshot.Lines.GetLineEnd(line);
                var tail = snapshot.GetText(TextRange.FromBounds(token.End, lineEnd)).Trim();
                if (tail.Length > 0)
                {
                    folds.Add(new FoldRange(
                        $"xml-element:{frame.Path}",
                        TextRange.FromBounds(frame.Token.End, token.Start),
                        " … "));
                    continue;
                }

                var end = line + 1 < snapshot.Lines.LineCount
                    ? snapshot.Lines.GetLineStart(line + 1)
                    : snapshot.Length;
                var closingTag = snapshot.GetText(TextRange.FromBounds(token.Start, token.End));
                folds.Add(new FoldRange(
                    $"xml-element:{frame.Path}",
                    TextRange.FromBounds(frame.Token.End, end),
                    $" … {closingTag}"));
            }
            else if (token.Kind == XmlTokenKind.EndElement && token.IsClosed)
            {
                isComplete = false;
            }
        }

        isComplete &= frames.Count == 0;
        folds.Sort(static (left, right) => left.Range.Start.CompareTo(right.Range.Start));
        return ValueTask.FromResult(new FoldingAnalysis(folds, isComplete));
    }

    private sealed class XmlFrame
    {
        private readonly Dictionary<string, int> _ordinals = new(StringComparer.Ordinal);

        public XmlFrame(XmlToken token, string path)
        {
            Token = token;
            Path = path;
        }

        public XmlToken Token { get; }
        public string Name => Token.Name;
        public string Path { get; }

        public int NextOrdinal(string name)
        {
            _ordinals.TryGetValue(name, out var ordinal);
            _ordinals[name] = ordinal + 1;
            return ordinal;
        }
    }
}
