namespace Azunyan.Core;

internal readonly record struct WrapBreakKey(
    int LogicalLine,
    int SourceStart,
    int SourceEnd);
