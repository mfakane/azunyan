namespace Azunote;

/// <summary>
/// Which file system entries a settings scan leaves alone, both for a scan
/// that holds each entry in hand and for a change notification that has only
/// a path to judge by.
/// </summary>
internal static class FileSystemScanPolicy
{
    /// <summary>
    /// Attributes that keep a directory out of a scan. Hidden covers the .git
    /// directory Git for Windows creates when a collection of definitions is
    /// cloned into the settings folder, whose contents are both irrelevant and
    /// expensive to walk. Reparse points are skipped because a scan has no
    /// other guard against a junction that points back at an ancestor.
    /// </summary>
    internal const FileAttributes SkippedDirectoryAttributes =
        FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint;

    /// <summary>
    /// Attributes that keep a file out of a scan. A file that is a reparse
    /// point still reads, having nothing to recurse into.
    /// </summary>
    internal const FileAttributes SkippedFileAttributes =
        FileAttributes.Hidden | FileAttributes.System;

    internal static bool IsSkippedDirectory(FileAttributes attributes) =>
        (attributes & SkippedDirectoryAttributes) != 0;

    internal static bool IsSkippedFile(FileAttributes attributes) =>
        (attributes & SkippedFileAttributes) != 0;

    /// <summary>
    /// Whether a scan rooted at <paramref name="root"/> would leave
    /// <paramref name="path"/> alone, by its own attributes or by those of a
    /// directory between it and the root. An entry that cannot be read says
    /// nothing about the entries above it, so the walk continues past it: a
    /// file deleted below a hidden directory is still recognized as one the
    /// scan never saw. A path outside the root is not the root's to judge
    /// and is reported as not skipped.
    /// </summary>
    internal static bool IsSkipped(string? path, string root)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        string fullRoot;
        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
        catch (Exception exception) when (
            exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }

        // Only the entries below the root are the root's to judge. Walking
        // past it would reach directories such as AppData, which Windows
        // marks hidden, and report every path outside the root as skipped.
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            || (fullPath.Length > fullRoot.Length
                && fullPath[fullRoot.Length] != Path.DirectorySeparatorChar
                && fullPath[fullRoot.Length] != Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        for (var current = fullPath;
             current is not null
                 && !string.Equals(current, fullRoot, StringComparison.OrdinalIgnoreCase);
             current = GetParentDirectory(current))
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
            {
                continue;
            }

            var skipped = (attributes & FileAttributes.Directory) != 0
                ? IsSkippedDirectory(attributes)
                : IsSkippedFile(attributes);
            if (skipped)
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetParentDirectory(string directory)
    {
        var parent = Path.GetDirectoryName(directory);
        return string.IsNullOrEmpty(parent)
            || string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)
                ? null
                : parent;
    }
}
