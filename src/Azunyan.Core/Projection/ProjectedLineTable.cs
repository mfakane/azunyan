namespace Azunyan.Core;

/// <summary>
/// A chunked immutable line sequence. Incremental projections can share
/// unaffected chunks and defer rebasing until a line is actually requested.
/// </summary>
internal sealed class ProjectedLineTable : IReadOnlyList<ProjectedLine>
{
    private const int ChunkSize = 256;
    private readonly ProjectedLineChunk[] _chunks;
    private readonly int[] _ends;

    private ProjectedLineTable(ProjectedLineChunk[] chunks)
    {
        _chunks = chunks;
        _ends = new int[chunks.Length];
        var count = 0;
        for (var index = 0; index < chunks.Length; index++)
        {
            count = checked(count + chunks[index].Count);
            _ends[index] = count;
        }

        Count = count;
    }

    public int Count { get; }

    public bool TryGetVisualLine(int logicalLine, out int visualLine)
    {
        visualLine = GetVisualLineAtOrAfter(logicalLine);
        return visualLine < Count && this[visualLine].LogicalLine == logicalLine;
    }

    public int GetVisualLineAtOrAfter(int logicalLine)
    {
        var low = 0;
        var high = Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (this[middle].LogicalLine < logicalLine)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    public ProjectedLine this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var chunkIndex = FindChunk(index);
            var chunkStart = chunkIndex == 0 ? 0 : _ends[chunkIndex - 1];
            return _chunks[chunkIndex][index - chunkStart];
        }
    }

    public IEnumerator<ProjectedLine> GetEnumerator()
    {
        foreach (var chunk in _chunks)
        {
            for (var index = 0; index < chunk.Count; index++)
            {
                yield return chunk[index];
            }
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    public static ProjectedLineTable FromLines(IReadOnlyList<ProjectedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var chunks = new List<ProjectedLineChunk>((lines.Count + ChunkSize - 1) / ChunkSize);
        for (var start = 0; start < lines.Count; start += ChunkSize)
        {
            chunks.Add(ProjectedLineChunk.Create(
                lines,
                start,
                Math.Min(ChunkSize, lines.Count - start)));
        }

        return new ProjectedLineTable(chunks.ToArray());
    }

    public static ProjectedLineTable FromChunks(IEnumerable<ProjectedLineChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        return new ProjectedLineTable(chunks.Where(chunk => chunk.Count > 0).ToArray());
    }

    public void AddRange(
        List<ProjectedLineChunk> destination,
        int start,
        int count,
        int logicalDelta = 0,
        int offsetDelta = 0)
    {
        if (start < 0 || count < 0 || start > Count - count)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        var remaining = count;
        var index = start;
        while (remaining > 0)
        {
            var chunkIndex = FindChunk(index);
            var chunkStart = chunkIndex == 0 ? 0 : _ends[chunkIndex - 1];
            var localStart = index - chunkStart;
            var take = Math.Min(remaining, _chunks[chunkIndex].Count - localStart);
            destination.Add(_chunks[chunkIndex].Slice(
                localStart,
                take,
                logicalDelta,
                offsetDelta));
            index += take;
            remaining -= take;
        }
    }

    private int FindChunk(int index)
    {
        var low = 0;
        var high = _ends.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_ends[middle] <= index)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}

internal sealed class ProjectedLineChunk
{
    private readonly IReadOnlyList<ProjectedLine> _source;
    private readonly int _start;
    private readonly int _logicalDelta;
    private readonly int _offsetDelta;
    private Dictionary<int, ProjectedLine>? _rebased;

    private ProjectedLineChunk(
        IReadOnlyList<ProjectedLine> source,
        int start,
        int count,
        int logicalDelta,
        int offsetDelta)
    {
        _source = source;
        _start = start;
        Count = count;
        _logicalDelta = logicalDelta;
        _offsetDelta = offsetDelta;
    }

    public int Count { get; }

    public ProjectedLine this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var sourceLine = _source[_start + index];
            if (_logicalDelta == 0 && _offsetDelta == 0)
            {
                return sourceLine;
            }

            _rebased ??= new Dictionary<int, ProjectedLine>();
            if (_rebased.TryGetValue(index, out var rebased))
            {
                return rebased;
            }

            rebased = sourceLine.Rebase(
                checked(sourceLine.LogicalLine + _logicalDelta),
                TextRange.FromBounds(
                    checked(sourceLine.SourceRange.Start + _offsetDelta),
                    checked(sourceLine.SourceRange.End + _offsetDelta)));
            _rebased[index] = rebased;
            return rebased;
        }
    }

    public static ProjectedLineChunk Create(
        IReadOnlyList<ProjectedLine> source,
        int start,
        int count) =>
        new(source, start, count, 0, 0);

    public ProjectedLineChunk Slice(
        int start,
        int count,
        int logicalDelta,
        int offsetDelta) =>
        new(
            _source,
            _start + start,
            count,
            checked(_logicalDelta + logicalDelta),
            checked(_offsetDelta + offsetDelta));
}
