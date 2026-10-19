namespace Prowl.Launcher;

/// <summary>
///     Replaces a directory with a staged copy, keeping the previous copy until the move succeeds.
///     Callers hold their operation lock throughout recovery and replacement.
/// </summary>
static class DirectoryReplacement
{
    /// <summary>Finishes or rolls back a replacement that was interrupted before its backup was removed.</summary>
    internal static void Recover(string target, Func<string, bool> isValid)
    {
        string backup = BackupPath(target);
        if (!Directory.Exists(backup))
        {
            return;
        }
        FileSystemHelper.RejectLink(backup);
        if (Directory.Exists(target))
        {
            FileSystemHelper.RejectLink(target);
            if (isValid(target))
            {
                FileSystemHelper.DeleteDirectory(backup);
                return;
            }
            // Retain both copies when neither can be validated.
            if (!isValid(backup))
            {
                throw new IOException("The interrupted update has no complete installation; its backup was retained.");
            }
            FileSystemHelper.DeleteDirectory(target);
        }
        Directory.Move(backup, target);
    }

    /// <summary>Replaces <paramref name="target" /> using a sibling backup that <see cref="Recover" /> understands.</summary>
    internal static void Replace(string staging, string target)
    {
        string backup = BackupPath(target);
        if (Directory.Exists(backup))
        {
            throw new IOException("Recover the previous update before replacing it.");
        }
        Swap(staging, target, backup);
    }

    /// <summary>Moves staging into target, keeping the old target at backup until the move succeeds.</summary>
    internal static void Swap(string staging, string target, string backup)
    {
        if (Directory.Exists(target))
        {
            FileSystemHelper.RejectLink(target);
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
            FileSystemHelper.DeleteDirectory(backup);
        }
    }

    private static string BackupPath(string target) => target + ".previous";
}
