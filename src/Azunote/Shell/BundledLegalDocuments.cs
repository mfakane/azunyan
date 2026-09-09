namespace Azunote;

internal static class BundledLegalDocuments
{
    internal static bool IsReadOnlyPath(string path, string? baseDirectory = null)
    {
        var root = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        var fullPath = Path.GetFullPath(path);
        return string.Equals(fullPath, Path.Combine(root, "LICENSE"), StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, Path.Combine(root, "THIRD-PARTY-NOTICES.md"), StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(
                Path.Combine(root, "licenses") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}
