using System.ComponentModel;
using System.Diagnostics;
namespace Prowl.Launcher;

static class ProcessHelper
{
    /// <summary>A windowless child process whose output the launcher reads.</summary>
    internal static ProcessStartInfo Captured(string executable, string? workingDirectory, params IEnumerable<string> arguments)
    {
        ProcessStartInfo info = new( executable )
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (workingDirectory != null)
        {
            info.WorkingDirectory = workingDirectory;
        }
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        return info;
    }

    /// <summary>Opens a URL or folder with the operating system's default handler.</summary>
    internal static void OpenInShell(string target) => Process.Start(new ProcessStartInfo(target)
    {
        UseShellExecute = true
    });

    /// <summary>
    ///     Whether a process named <paramref name="processName" /> runs from inside <paramref name="root" />.
    ///     A process we cannot inspect is treated as running so callers never replace files in use.
    /// </summary>
    internal static bool IsRunningUnder(string processName, string root)
    {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (FileSystemHelper.IsInside(root, process.MainModule?.FileName))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while we inspected it.
                }
                catch (Win32Exception)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    ///     A unique pipe name. Absolute Unix pipe paths avoid adding a potentially long TMPDIR and
    ///     CoreFxPipe_ prefix, which can exceed the socket path limit.
    /// </summary>
    internal static string PipeName(string prefix)
    {
        string name = prefix + Guid.NewGuid().ToString("N");
        return OperatingSystem.IsWindows() ? name : Path.Combine("/tmp", name);
    }
}
