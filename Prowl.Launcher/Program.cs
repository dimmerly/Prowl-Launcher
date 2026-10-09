using System.Diagnostics;

namespace Prowl.Launcher;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
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
                args.Contains("--uninstall-preview") );
            launcher.Run("Prowl Launcher", 1200, 840);
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

    private static bool ForwardToUpdatedLauncher(LauncherStore store, string[] args)
    {
        string? updated = store.Settings.LauncherExecutable;
        if (args.Contains("--screenshot") || updated == null || !File.Exists(updated)
            || LauncherStore.PathsEqual(updated, Environment.ProcessPath!))
        {
            return false;
        }

        string versions = Path.Combine(store.Home, "LauncherVersions");
        LauncherStore.SafeChildPath(versions, Path.GetRelativePath(versions, updated));
        ProcessStartInfo info = new( updated )
        {
            UseShellExecute = false
        };
        foreach (string argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        Process.Start(info);
        return true;
    }

}
