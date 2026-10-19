using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Prowl.Launcher;

internal static class ProcessHelper
{
    /// <summary>A windowless child process whose output the launcher reads.</summary>
    internal static ProcessStartInfo Captured(string executable, string? workingDirectory,
        params IEnumerable<string> arguments)
    {
        ProcessStartInfo info = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
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
    internal static void OpenInShell(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

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
                    if (FileSystemHelper.IsInside(root, ExecutablePath(process)))
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
    ///     The executable a process runs, or null once it has exited. Throws <see cref="Win32Exception" /> when the
    ///     process can't be inspected.
    /// </summary>
    private static string? ExecutablePath(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return process.MainModule?.FileName;
        }

        // MainModule enumerates modules, which fails while a process is still starting or already exiting.
        // QueryFullProcessImageName only needs limited query rights and works throughout.
        const uint QueryLimitedInformation = 0x1000;
        const int InvalidParameter = 87;
        using SafeProcessHandle handle = OpenProcess(QueryLimitedInformation, false, process.Id);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            // An invalid process id means it already exited.
            return error == InvalidParameter ? null : throw new Win32Exception(error);
        }

        char[] buffer = new char[32768];
        int length = buffer.Length;
        if (!QueryFullProcessImageName(handle, 0, buffer, ref length))
        {
            if (process.HasExited)
            {
                return null;
            }

            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new string(buffer, 0, length);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, char[] name,
        ref int size);

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
