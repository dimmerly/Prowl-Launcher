using System.Diagnostics;

namespace Prowl.Launcher;

static class SampleHelper
{
    public static ProcessStartInfo LaunchInfo(string assemblyPath)
    {
        string directory = Path.GetDirectoryName(assemblyPath)!;
        ProcessStartInfo launch = new( Path.Combine(directory, OperatingSystem.IsWindows() ? "Prowl.SampleHost.exe" : "Prowl.SampleHost") )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory
        };
        launch.ArgumentList.Add("--run-sample");
        launch.ArgumentList.Add(assemblyPath);
        return launch;
    }
}
