using System.Diagnostics;

namespace Prowl.Launcher;

// Callers hold their operation lock throughout recovery and replacement.
static class DirectoryReplacementService
{
    internal static void Recover(string target, Func<string, bool> isValid)
    {
        string backup = target + ".previous";
        if (!Directory.Exists(backup))
        {
            return;
        }
        RejectLinks(backup);
        if (Directory.Exists(target))
        {
            RejectLinks(target);
            if (isValid(target))
            {
                Directory.Delete(backup, true);
                return;
            }
            // Retain both copies when neither can be validated.
            if (!isValid(backup))
            {
                throw new IOException("The interrupted update has no complete installation; its backup was retained.");
            }
            Directory.Delete(target, true);
        }
        Directory.Move(backup, target);
    }

    internal static void Replace(string staging, string target)
    {
        string backup = target + ".previous";
        if (Directory.Exists(backup))
        {
            throw new IOException("Recover the previous update before replacing it.");
        }
        Swap(staging, target, backup);
    }

    // Moves staging into target, keeping the old target at backup until the move succeeds.
    internal static void Swap(string staging, string target, string backup)
    {
        if (Directory.Exists(target))
        {
            RejectLinks(target);
            Directory.Move(target, backup);
        }
        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            if (Directory.Exists(backup))
            {
                Directory.Move(backup, target);
            }
            throw;
        }
        if (Directory.Exists(backup))
        {
            Directory.Delete(backup, true);
        }
    }

    // A process we cannot inspect is treated as running so callers never replace files in use.
    internal static bool IsRunningUnder(string processName, string root)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is {} file && file.StartsWith(prefix, comparison))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return true;
                }
            }
        }
        return false;
    }

    internal static void RejectLinks(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? path))
        {
            FileAttributes attributes = File.GetAttributes(path);
            RejectLink(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (string child in Directory.EnumerateFileSystemEntries(path))
                {
                    pending.Push(child);
                }
            }
        }
    }

    internal static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Update directories cannot contain symbolic links or junctions.");
        }
    }
}
