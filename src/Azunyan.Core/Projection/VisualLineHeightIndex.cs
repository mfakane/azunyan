namespace Azunyan.Core;

/// <summary>
/// Prefix-sum index for variable-height visual lines. It supports viewport
/// lookup without scanning every line after folding, wrapping, or block
/// adornments change a local height.
/// </summary>
public sealed class VisualLineHeightIndex
{
    private readonly ChunkedHeightIndex _index;

    public VisualLineHeightIndex(IEnumerable<double> heights)
    {
        ArgumentNullException.ThrowIfNull(heights);
        _index = new ChunkedHeightIndex(heights);
    }

    private VisualLineHeightIndex(ChunkedHeightIndex index) => _index = index;

    public static VisualLineHeightIndex CreateUniform(int count, double height) =>
        new(ChunkedHeightIndex.CreateUniform(count, height));

    public int Count => _index.Count;

    public double TotalHeight => _index.TotalHeight;

    public double GetHeight(int line) => _index.GetHeight(line);

    public double GetOffset(int line) => _index.GetOffset(line);

    public void SetHeight(int line, double height) => _index.SetHeight(line, height);

    public void Splice(int index, int removeCount, IEnumerable<double> insertedHeights) =>
        _index.Splice(index, removeCount, insertedHeights);

    /// <summary>
    /// Returns the line containing the given vertical offset. An offset at the
    /// exact document end resolves to the final line.
    /// </summary>
    public int FindLine(double offset)
    {
        return _index.FindLine(offset);
    }
}
