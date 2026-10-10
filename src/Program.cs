using System.Diagnostics;

namespace Prowl.Launcher;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            LauncherStartupService.CaptureReadyPipe();
            LauncherStore store = new();
            if (ForwardToUpdatedLauncher(store, args))
            {
                return;
            }

            string? screenshot = null;
            int tab = 0;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--screenshot" when i + 1 < args.Length:
                        screenshot = args[++i];
                        break;
                    case "--versions":
                        tab = 1;
                        break;
                    case "--settings":
                        tab = 2;
                        break;
                    case "--samples":
                        tab = 3;
                        break;
                }
            }

            Launcher launcher = new(
                store,
                screenshot,
                tab,
                args.Contains("--offline"),
                args.Contains("--progress-preview"),
                args.Contains("--alert-preview"),
                args.Contains("--notes-preview"),
                args.Contains("--new-project"),
                args.Contains("--uninstall-preview"),
                args.Contains("--installation-preview") );
            launcher.Run(Constants.Layout.WindowTitle, Constants.Layout.WindowWidth, Constants.Layout.WindowHeight);
        }
        catch (Exception exception)
        {
            string path = Path.Combine(Platform.DefaultHome, "startup-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:o} {exception}\n");
            Console.Error.WriteLine(exception);
            Environment.ExitCode = 1;
        }
    }

    internal static bool ForwardToUpdatedLauncher(LauncherStore store, string[] args)
    {
        string? updated = store.Settings.LauncherExecutable;
        if (LauncherStartupService.ReadyPipe != null || args.Contains("--screenshot") || updated == null
            || LauncherStore.PathsEqual(updated, Environment.ProcessPath!))
        {
            return false;
        }

        try
        {
            string versions = LauncherUpdaterService.UpdatesPath(store);
            LauncherStore.SafeChildPath(versions, Path.GetRelativePath(versions, updated));
            LauncherStartupService.StartAsync(updated, store.Home, args).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception
            or OperationCanceledException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            store.Settings.LauncherExecutable = null;
            store.Save();
            File.AppendAllText(Path.Combine(store.Home, "launcher.log"), $"{DateTimeOffset.UtcNow:o} Update fallback: {exception}\n");
            return false;
        }
    }
}
