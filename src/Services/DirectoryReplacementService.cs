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
