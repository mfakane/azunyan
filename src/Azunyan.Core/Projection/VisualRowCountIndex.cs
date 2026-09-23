namespace Azunyan.Core;

internal sealed class VisualRowCountIndex
{
    private const int ChunkSize = 256;
    private readonly CountChunk[] _chunks;
    private readonly int[] _lineEnds;
    private readonly int[] _rowEnds;
    private readonly int? _uniformCount;

    private VisualRowCountIndex(CountChunk[] chunks)
    {
        _chunks = chunks;
        _lineEnds = new int[chunks.Length];
        _rowEnds = new int[chunks.Length];
        for (var index = 0; index < chunks.Length; index++)
        {
            Count = checked(Count + chunks[index].Count);
            TotalRows = checked(TotalRows + chunks[index].Total);
            _lineEnds[index] = Count;
            _rowEnds[index] = TotalRows;
        }
    }

    private VisualRowCountIndex(int count, int uniformCount)
    {
        _chunks = Array.Empty<CountChunk>();
        _lineEnds = Array.Empty<int>();
        _rowEnds = Array.Empty<int>();
        Count = count;
        TotalRows = checked(count * uniformCount);
        _uniformCount = uniformCount;
    }

    public int Count { get; private set; }

    public int TotalRows { get; private set; }

    public static VisualRowCountIndex Create(IReadOnlyList<int> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Count == 0)
        {
            return new VisualRowCountIndex(0, 1);
        }

        var first = counts[0];
        if (first <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(counts));
        }

        if (counts.All(count => count == first))
        {
            return new VisualRowCountIndex(counts.Count, first);
        }

        var chunks = new List<CountChunk>((counts.Count + ChunkSize - 1) / ChunkSize);
        for (var start = 0; start < counts.Count; start += ChunkSize)
        {
            chunks.Add(CountChunk.Create(counts, start, Math.Min(ChunkSize, counts.Count - start)));
        }

        return new VisualRowCountIndex(chunks.ToArray());
    }

    public static VisualRowCountIndex CreateUniform(int count, int rowsPerLine)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowsPerLine);
        return new VisualRowCountIndex(count, rowsPerLine);
    }

    public (int Start, int End) GetRowRange(int startLine, int endLine)
    {
        if (startLine < 0 || endLine < startLine || endLine > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine));
        }

        return (GetRowOffset(startLine), GetRowOffset(endLine));
    }

    public VisualRowCountIndex Replace(int index, int removeCount, IReadOnlyList<int> insertedCounts)
    {
        ArgumentNullException.ThrowIfNull(insertedCounts);
        if (index < 0 || removeCount < 0 || index > Count - removeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (_uniformCount is { } uniformCount
            && insertedCounts.All(count => count == uniformCount))
        {
            return new VisualRowCountIndex(
                checked(Count - removeCount + insertedCounts.Count),
                uniformCount);
        }
        if (_uniformCount is { } materializedCount)
        {
            return CreateChunkedUniform(Count, materializedCount)
                .Replace(index, removeCount, insertedCounts);
        }

        var chunks = new List<CountChunk>(_chunks.Length + 2);
        AddRange(chunks, 0, index);
        for (var start = 0; start < insertedCounts.Count; start += ChunkSize)
        {
            Append(chunks, CountChunk.Create(
                insertedCounts,
                start,
                Math.Min(ChunkSize, insertedCounts.Count - start)));
        }

        AddRange(chunks, index + removeCount, Count - index - removeCount);
        return new VisualRowCountIndex(chunks.ToArray());
    }

    private static VisualRowCountIndex CreateChunkedUniform(int count, int value)
    {
        var chunks = new List<CountChunk>((count + ChunkSize - 1) / ChunkSize);
        for (var start = 0; start < count; start += ChunkSize)
        {
            var values = new int[Math.Min(ChunkSize, count - start)];
            Array.Fill(values, value);
            chunks.Add(CountChunk.Create(values, 0, values.Length));
        }

        return new VisualRowCountIndex(chunks.ToArray());
    }

    private int GetRowOffset(int line)
    {
        if (line == Count) return TotalRows;
        if (line == 0) return 0;
        if (_uniformCount is { } uniformCount) return checked(line * uniformCount);
        var chunkIndex = FindChunk(line);
        var lineStart = chunkIndex == 0 ? 0 : _lineEnds[chunkIndex - 1];
        var rows = chunkIndex == 0 ? 0 : _rowEnds[chunkIndex - 1];
        return checked(rows + _chunks[chunkIndex].Prefix(line - lineStart));
    }

    private void AddRange(List<CountChunk> destination, int start, int count)
    {
        for (var index = start; count > 0;)
        {
            var chunkIndex = FindChunk(index);
            var chunkStart = chunkIndex == 0 ? 0 : _lineEnds[chunkIndex - 1];
            var localStart = index - chunkStart;
            var take = Math.Min(count, _chunks[chunkIndex].Count - localStart);
            Append(destination, _chunks[chunkIndex].Slice(localStart, take));
            index += take;
            count -= take;
        }
    }

    private static void Append(List<CountChunk> destination, CountChunk chunk)
    {
        if (destination.Count > 0
            && destination[^1].Count + chunk.Count <= ChunkSize)
        {
            destination[^1] = destination[^1].Concat(chunk);
            return;
        }

        destination.Add(chunk);
    }

    private int FindChunk(int line)
    {
        var low = 0;
        var high = _lineEnds.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_lineEnds[middle] <= line) low = middle + 1;
            else high = middle;
        }

        return low;
    }

    private sealed class CountChunk
    {
        private readonly int[] _values;

        private CountChunk(int[] values)
        {
            _values = values;
            Total = values.Sum();
        }

        public int Count => _values.Length;
        public int Total { get; }

        public static CountChunk Create(IReadOnlyList<int> values, int start, int count)
        {
            var copy = new int[count];
            for (var index = 0; index < count; index++)
            {
                var value = values[start + index];
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(values));
                copy[index] = value;
            }

            return new CountChunk(copy);
        }

        public int Prefix(int count)
        {
            var total = 0;
            for (var index = 0; index < count; index++) total = checked(total + _values[index]);
            return total;
        }

        public CountChunk Slice(int start, int count)
        {
            if (start == 0 && count == Count) return this;
            var values = new int[count];
            Array.Copy(_values, start, values, 0, count);
            return new CountChunk(values);
        }

        public CountChunk Concat(CountChunk other)
        {
            var values = new int[Count + other.Count];
            Array.Copy(_values, values, Count);
            Array.Copy(other._values, 0, values, Count, other.Count);
            return new CountChunk(values);
        }
    }
}
