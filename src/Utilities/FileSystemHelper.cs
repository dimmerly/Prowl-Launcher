using Prowl.Rosetta;

namespace Prowl.Launcher;

/// <summary>Path comparison, containment and link checks for the folders the launcher manages.</summary>
internal static class FileSystemHelper
{
    internal static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparison);

    /// <summary>Whether <paramref name="path" /> is strictly inside <paramref name="root" />.</summary>
    internal static bool IsInside(string root, string? path) =>
        path != null && Path.GetFullPath(path).StartsWith(WithTrailingSeparator(root), PathComparison);

    /// <summary>Resolves <paramref name="relative" /> under <paramref name="root" />, rejecting anything that escapes it.</summary>
    public static string SafeChildPath(string root, string relative)
    {
        string fullRoot = WithTrailingSeparator(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (Path.IsPathRooted(relative) || !fullPath.StartsWith(fullRoot, PathComparison))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.path_outside_folder"));
        }

        return fullPath;
    }

    /// <summary>
    ///     Deletes a directory tree, including read-only files (such as git objects), which Windows otherwise refuses
    ///     to delete. Links are removed, not followed.
    /// </summary>
    internal static void DeleteDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            EnumerationOptions options = new()
            {
                // Hidden folders such as .git are included; links are not entered.
                RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (FileInfo file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                if (file.IsReadOnly)
                {
                    file.IsReadOnly = false;
                }
            }
        }

        Directory.Delete(path, true);
    }

    internal static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>Checks only the path itself; use <see cref="RejectLinks" /> for a whole tree.</summary>
    internal static void RejectLink(string path)
    {
        if (IsLink(path))
        {
            throw new IOException("Update directories cannot contain symbolic links or junctions.");
        }
    }

    internal static void RejectLinks(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? path))
        {
            RejectLink(path);
            if (Directory.Exists(path))
            {
                foreach (string child in Directory.EnumerateFileSystemEntries(path))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static string WithTrailingSeparator(string root) =>
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
