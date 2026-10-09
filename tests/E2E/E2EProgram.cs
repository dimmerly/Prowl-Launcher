using System.IO.Pipes;
using System.Text.Json;

namespace Prowl.Launcher.Test.E2E;

/// <summary>Child-process entry point for GUI scenarios and editor/update launch probes.</summary>
internal static class E2EProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? home = Environment.GetEnvironmentVariable("PROWL_LAUNCHER_HOME");
        string? executable = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
        if (executable == "Prowl.Editor")
        {
            File.WriteAllText(Path.Combine(home!, "editor-launch.json"),
                JsonSerializer.Serialize(new { Arguments = args, WorkingDirectory = Environment.CurrentDirectory }));
            return 0;
        }
        if (executable == "Prowl.Launcher") return UpdateProbe(home!).GetAwaiter().GetResult();
        if (args.Length != 3 || args[0] != "--e2e")
        {
            Console.Error.WriteLine("Use dotnet test, or --e2e <scenario> <isolated-directory>.");
            return 2;
        }
        try
        {
            LauncherFixture.InstallSoftwareGraphics();
            using LauncherFixture fixture = new(args[2]);
            LauncherScenarios.Prepare(args[1], fixture).GetAwaiter().GetResult();
            Launcher launcher = new(fixture.Store, offline: !args[1].StartsWith("Startup", StringComparison.Ordinal));
            UiDriver.GetField<HttpClient>(launcher, "_http").Dispose();
            UiDriver.SetField(launcher, "_http", fixture.Http);
            UiDriver driver = new(launcher, Path.Combine(fixture.Root, "diagnostics"));
            driver.Attach(ui => LauncherScenarios.Run(args[1], fixture, ui));
            launcher.Run("Prowl Launcher E2E", 1200, 1100);
            if (driver.Failure != null) throw driver.Failure;
            UiDriver.Check(driver.Completed || driver.ExpectsClose, "The launcher closed before the scenario completed.");
            LauncherScenarios.AfterClose(args[1], fixture);
            Console.WriteLine("PASS " + args[1]);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task<int> UpdateProbe(string home)
    {
        await File.WriteAllTextAsync(Path.Combine(home, "update-probe.pid"), Environment.ProcessId.ToString());
        if (Environment.GetEnvironmentVariable("PROWL_E2E_UPDATE_FAIL") == "1") return 23;
        string pipeName = Environment.GetEnvironmentVariable("PROWL_LAUNCHER_READY_PIPE")!;
        await using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10000);
        await pipe.WriteAsync(new byte[] { 1 });
        await pipe.FlushAsync();
        await Task.Delay(Timeout.Infinite);
        return 0;
    }
}
