using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Prowl.Samples;

internal static class SampleRunner
{
    public static void Run(string assemblyPath, string[] args)
    {
        int preview = Array.IndexOf(args, "--sample-preview");
        if (preview >= 0 && preview + 1 < args.Length)
            SamplePreviewHelper.Capture(args[preview + 1]);
        string launcherDirectory = AppContext.BaseDirectory;
        // Vendored audio libraries live under runtimes rather than the application root.
        NativeLibrary.SetDllImportResolver(typeof(Prowl.Runtime.Game).Assembly, (name, _, _) =>
        {
            if (name != "miniaudioex")
                return IntPtr.Zero;
            string file = OperatingSystem.IsWindows() ? "miniaudioex.dll"
                : OperatingSystem.IsMacOS() ? "libminiaudioex.dylib" : "libminiaudioex.so";
            string path = Path.Combine(launcherDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", file);
            return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
        });
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        MethodInfo entry = assembly.EntryPoint ?? throw new InvalidDataException("Missing sample entry point.");
        object? result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : [args]);
        if (result is Task task)
            task.GetAwaiter().GetResult();
        if (result is int exitCode)
            Environment.ExitCode = exitCode;
    }
}
