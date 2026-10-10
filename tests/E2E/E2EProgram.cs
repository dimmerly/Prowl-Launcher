using System.IO.Pipes;
using System.Text.Json;

namespace Prowl.Launcher.Test.E2E;

/// <summary>Child-process entry point for GUI scenarios and editor/update launch probes.</summary>
static class E2EProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? home = Environment.GetEnvironmentVariable("PROWL_LAUNCHER_HOME");
        string? executable = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
        if (args.FirstOrDefault() == "--console-probe")
        {
            return ConsoleProbe(args.Skip(1).ToArray()).GetAwaiter().GetResult();
        }
        if (executable == "Prowl.SampleHost")
        {
            File.AppendAllText(Path.Combine(home!, "sample-launch.log"), JsonSerializer.Serialize(args) + "\n");
            return 0;
        }
        if (executable == "Prowl.Editor")
        {
            File.WriteAllText(Path.Combine(home!, "editor-launch.json"),
                JsonSerializer.Serialize(new
                {
                    Arguments = args, WorkingDirectory = Environment.CurrentDirectory
                }));
            return ConsoleProbe([]).GetAwaiter().GetResult();
        }
        if (executable == "Prowl.Launcher")
        {
            return UpdateProbe(home!).GetAwaiter().GetResult();
        }
        if (args.Length != 3 || args[0] != "--e2e")
        {
            Console.Error.WriteLine("Use dotnet test, or --e2e <scenario> <isolated-directory>.");
            return 2;
        }
        try
        {
            LauncherFixture.InstallSoftwareGraphics();
            using LauncherFixture fixture = new( args[2] );
            LauncherScenarios.Prepare(args[1], fixture).GetAwaiter().GetResult();
            Launcher launcher = new( fixture.Store, offline: !args[1].StartsWith("Startup", StringComparison.Ordinal)
                                                             && !args[1].StartsWith("Sample", StringComparison.Ordinal) );
            UiDriver.GetField<HttpClient>(launcher, "_http").Dispose();
            UiDriver.SetField(launcher, "_http", fixture.Http);
            UiDriver driver = new( launcher, Path.Combine(fixture.Root, "diagnostics") );
            driver.Attach(ui => LauncherScenarios.Run(args[1], fixture, ui));
            launcher.Run("Prowl Launcher E2E", 1200, 1100);
            if (driver.Failure != null)
            {
                throw driver.Failure;
            }
            UiDriver.Check(driver.Completed || driver.ExpectsClose, "The launcher closed before the scenario completed.");
            LauncherScenarios.AfterClose(args[1], fixture, launcher);
            Console.WriteLine("PASS " + args[1]);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task<int> ConsoleProbe(string[] args)
    {
        if (OperatingSystem.IsWindows()) Console.Title = "Editor console probe";
        Console.WriteLine("\u001b[32mLoading models\u001b[0m");
        Console.Error.WriteLine("Warning: missing material");
        if (args.Contains("fail") || Environment.GetEnvironmentVariable("PROWL_E2E_EDITOR_FAIL") == "1") return 7;
        await Task.Delay(Environment.GetEnvironmentVariable("PROWL_E2E_EDITOR_SLOW") == "1" ? 3000 : 750);
        string? pipeName = Environment.GetEnvironmentVariable("PROWL_EDITOR_READY_PIPE");
        if (pipeName != null)
        {
            await using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(10000);
            await pipe.WriteAsync(new byte[] { 1 });
            await pipe.FlushAsync();
        }
        Console.WriteLine("Project ready");
        await Task.Delay(1000);
        for (int i = 0; i < 1500; i++) Console.WriteLine("Model " + i);
        Console.WriteLine("Final editor output");
        if (args.FirstOrDefault(arg => arg.StartsWith("completed-pipe=", StringComparison.Ordinal)) is {} completion)
        {
            await using NamedPipeClientStream pipe = new(".", completion["completed-pipe=".Length..], PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(10000);
            await pipe.WriteAsync(new byte[] { 1 });
            await pipe.FlushAsync();
        }
        return 0;
    }

    private static async Task<int> UpdateProbe(string home)
    {
        await File.WriteAllTextAsync(Path.Combine(home, "update-probe.pid"), Environment.ProcessId.ToString());
        if (Environment.GetEnvironmentVariable("PROWL_E2E_UPDATE_FAIL") == "1")
        {
            return 23;
        }
        string pipeName = Environment.GetEnvironmentVariable("PROWL_LAUNCHER_READY_PIPE")!;
        await using NamedPipeClientStream pipe = new( ".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous );
        await pipe.ConnectAsync(10000);
        await pipe.WriteAsync(new byte[]
        {
            1
        });
        await pipe.FlushAsync();
        await Task.Delay(Timeout.Infinite);
        return 0;
    }
}
