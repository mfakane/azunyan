namespace Azunyan.Core;

internal sealed class ChunkedHeightIndex
{
    private const int ChunkSize = 256;
    private HeightChunk[] _chunks = Array.Empty<HeightChunk>();
    private int[] _ends = Array.Empty<int>();
    private double[] _totals = Array.Empty<double>();
    private double[] _tree = Array.Empty<double>();

    public ChunkedHeightIndex(IEnumerable<double> heights)
    {
        ArgumentNullException.ThrowIfNull(heights);
        var values = heights.ToArray();
        ValidateValues(values);
        Rebuild(CreateChunks(values));
    }

    private ChunkedHeightIndex(HeightChunk[] chunks)
    {
        Rebuild(chunks);
    }

    public int Count { get; private set; }

    public double TotalHeight => Prefix(_chunks.Length);

    public static ChunkedHeightIndex CreateUniform(int count, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ValidateHeight(height, 0);
        var chunks = new List<HeightChunk>((count + ChunkSize - 1) / ChunkSize);
        for (var start = 0; start < count; start += ChunkSize)
        {
            var values = new double[Math.Min(ChunkSize, count - start)];
            Array.Fill(values, height);
            chunks.Add(new HeightChunk(values));
        }

        return new ChunkedHeightIndex(chunks.ToArray());
    }

    public double GetHeight(int line)
    {
        var (chunk, local) = Locate(line);
        return _chunks[chunk].Values[local];
    }

    public double GetOffset(int line)
    {
        if (line < 0 || line > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        if (line == Count)
        {
            return TotalHeight;
        }

        var chunk = FindChunk(line);
        var start = chunk == 0 ? 0 : _ends[chunk - 1];
        var sum = Prefix(chunk);
        for (var index = start; index < line; index++)
        {
            sum += _chunks[chunk].Values[index - start];
        }

        return sum;
    }

    public void SetHeight(int line, double height)
    {
        var (chunk, local) = Locate(line);
        ValidateHeight(height, line);
        var delta = height - _chunks[chunk].Values[local];
        _chunks[chunk].Values[local] = height;
        _totals[chunk] += delta;
        Add(chunk, delta);
    }

    public int FindLine(double offset)
    {
        if (Count == 0)
        {
            throw new InvalidOperationException("The height index contains no lines.");
        }

        if (!double.IsFinite(offset) || offset < 0 || offset > TotalHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (offset == TotalHeight)
        {
            return Count - 1;
        }

        var low = 0;
        var high = _chunks.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (Prefix(middle) + _totals[middle] <= offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var chunk = low;
        var line = chunk == 0 ? 0 : _ends[chunk - 1];
        var localOffset = offset - Prefix(chunk);
        var values = _chunks[chunk].Values;
        for (var index = 0; index < values.Length; index++)
        {
            if (localOffset < values[index])
            {
                return line + index;
            }

            localOffset -= values[index];
        }

        return Math.Min(Count - 1, line + values.Length - 1);
    }

    public void Splice(int index, int removeCount, IEnumerable<double> insertedHeights)
    {
        ArgumentNullException.ThrowIfNull(insertedHeights);
        if (index < 0 || index > Count || removeCount < 0 || index + removeCount > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var inserted = insertedHeights.ToArray();
        ValidateValues(inserted);
        var chunks = new List<HeightChunk>();
        AddRange(chunks, 0, index);
        chunks.AddRange(CreateChunks(inserted));
        AddRange(chunks, index + removeCount, Count - index - removeCount);
        Rebuild(chunks.ToArray());
    }

    private void AddRange(List<HeightChunk> destination, int start, int count)
    {
        var remaining = count;
        var index = start;
        while (remaining > 0)
        {
            var chunkIndex = FindChunk(index);
            var chunkStart = chunkIndex == 0 ? 0 : _ends[chunkIndex - 1];
            var localStart = index - chunkStart;
            var take = Math.Min(remaining, _chunks[chunkIndex].Values.Length - localStart);
            destination.Add(_chunks[chunkIndex].Slice(localStart, take));
            index += take;
            remaining -= take;
        }
    }

    private void Rebuild(HeightChunk[] chunks)
    {
        _chunks = chunks;
        _ends = new int[chunks.Length];
        _totals = new double[chunks.Length];
        Count = 0;
        for (var index = 0; index < chunks.Length; index++)
        {
            Count = checked(Count + chunks[index].Values.Length);
            _ends[index] = Count;
            _totals[index] = chunks[index].Values.Sum();
        }

        _tree = new double[chunks.Length + 1];
        for (var index = 0; index < _totals.Length; index++)
        {
            Add(index, _totals[index]);
        }
    }

    private (int Chunk, int Local) Locate(int line)
    {
        if (line < 0 || line >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        var chunk = FindChunk(line);
        var start = chunk == 0 ? 0 : _ends[chunk - 1];
        return (chunk, line - start);
    }

    private int FindChunk(int line)
    {
        var low = 0;
        var high = _ends.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_ends[middle] <= line)
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

    private double Prefix(int chunkCount)
    {
        var sum = 0d;
        var index = chunkCount;
        while (index > 0)
        {
            sum += _tree[index];
            index -= index & -index;
        }

        return sum;
    }

    private void Add(int zeroBasedChunk, double value)
    {
        for (var index = zeroBasedChunk + 1; index < _tree.Length; index += index & -index)
        {
            _tree[index] += value;
        }
    }

    private static HeightChunk[] CreateChunks(double[] values)
    {
        var chunks = new List<HeightChunk>((values.Length + ChunkSize - 1) / ChunkSize);
        for (var start = 0; start < values.Length; start += ChunkSize)
        {
            var count = Math.Min(ChunkSize, values.Length - start);
            var chunk = new double[count];
            for (var index = 0; index < count; index++)
            {
                chunk[index] = values[start + index];
            }

            chunks.Add(new HeightChunk(chunk));
        }

        return chunks.ToArray();
    }

    private static void ValidateValues(double[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            ValidateHeight(values[index], index);
        }
    }

    private static void ValidateHeight(double height, int line)
    {
        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), $"Line {line} must have a positive finite height.");
        }
    }

    private sealed class HeightChunk
    {
        public HeightChunk(double[] values) => Values = values;

        public double[] Values { get; }

        public HeightChunk Slice(int start, int count)
        {
            var values = new double[count];
            Array.Copy(Values, start, values, 0, count);
            return new HeightChunk(values);
        }
    }
}
