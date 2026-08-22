namespace Azunyan.Core;

/// <summary>
/// Prefix-sum index for variable-height visual lines. It supports viewport
/// lookup without scanning every line after folding, wrapping, or block
/// adornments change a local height.
/// </summary>
public sealed class VisualLineHeightIndex
{
    private readonly double[] _heights;
    private readonly double[] _tree;

    public VisualLineHeightIndex(IEnumerable<double> heights)
    {
        ArgumentNullException.ThrowIfNull(heights);
        _heights = heights.ToArray();
        _tree = new double[_heights.Length + 1];
        for (var index = 0; index < _heights.Length; index++)
        {
            ValidateHeight(_heights[index], index);
            Add(index, _heights[index]);
        }
    }

    public int Count => _heights.Length;

    public double TotalHeight => GetOffset(Count);

    public double GetHeight(int line)
    {
        ValidateLine(line);
        return _heights[line];
    }

    public double GetOffset(int line)
    {
        if (line < 0 || line > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        var sum = 0d;
        var index = line;
        while (index > 0)
        {
            sum += _tree[index];
            index -= index & -index;
        }

        return sum;
    }

    public void SetHeight(int line, double height)
    {
        ValidateLine(line);
        ValidateHeight(height, line);
        var delta = height - _heights[line];
        _heights[line] = height;
        Add(line, delta);
    }

    /// <summary>
    /// Returns the line containing the given vertical offset. An offset at the
    /// exact document end resolves to the final line.
    /// </summary>
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

        var index = 0;
        var sum = 0d;
        var step = HighestPowerOfTwoAtMost(Count);
        while (step != 0)
        {
            var next = index + step;
            if (next <= Count && sum + _tree[next] <= offset)
            {
                index = next;
                sum += _tree[next];
            }

            step >>= 1;
        }

        return Math.Min(index, Count - 1);
    }

    private void Add(int zeroBasedLine, double value)
    {
        for (var index = zeroBasedLine + 1; index < _tree.Length; index += index & -index)
        {
            _tree[index] += value;
        }
    }

    private void ValidateLine(int line)
    {
        if (line < 0 || line >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }
    }

    private static void ValidateHeight(double height, int line)
    {
        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), $"Line {line} must have a positive finite height.");
        }
    }

    private static int HighestPowerOfTwoAtMost(int value)
    {
        var result = 1;
        while (result <= value / 2)
        {
            result <<= 1;
        }

        return result;
    }
}
