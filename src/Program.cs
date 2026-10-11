using System.ComponentModel;
namespace Prowl.Launcher;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == EditorConsoleService.CollectorArgument)
            {
                try
                {
                    Environment.ExitCode = EditorConsoleCollector.RunAsync(args[1]).GetAwaiter().GetResult();
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(error.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }
            LauncherStartupService.CaptureReadyPipe();
            LauncherStore store = new();
            // Source builds share the user's settings, so they would otherwise hand off to the installed update.
            if (LauncherInstallationService.IsSelfInstallBuild && ForwardToUpdatedLauncher(store, args))
            {
                return;
            }

            string? screenshot = null;
            LauncherTab tab = LauncherTab.Projects;
            string? project = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--screenshot" when i + 1 < args.Length:
                        screenshot = args[++i];
                        break;
                    case "--versions":
                        tab = LauncherTab.Versions;
                        break;
                    case "--settings":
                    case LauncherInstallationService.UninstallArgument:
                        tab = LauncherTab.Settings;
                        break;
                    case "--samples":
                        tab = LauncherTab.Samples;
                        break;
                    case { } path when !path.StartsWith("--", StringComparison.Ordinal):
                        // A project folder or .prowl file, e.g. from a file association or drag onto the app icon.
                        project ??= Path.GetFullPath(path);
                        break;
                }
            }

            Launcher launcher = new(
                store,
                screenshot,
                tab,
                args.Contains("--offline"),
                new LauncherPreviews(
                    args.Contains("--progress-preview"),
                    args.Contains("--alert-preview"),
                    args.Contains("--notes-preview"),
                    args.Contains("--new-project"),
                    args.Contains("--uninstall-preview"),
                    args.Contains("--installation-preview")),
                args.Contains(LauncherInstallationService.UninstallArgument),
                project);
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
            || FileSystemHelper.PathsEqual(updated, Environment.ProcessPath!))
        {
            return false;
        }

        try
        {
            string versions = LauncherUpdaterService.UpdatesPath(store);
            FileSystemHelper.SafeChildPath(versions, Path.GetRelativePath(versions, updated));
            LauncherStartupService.StartAsync(updated, store.Home, args).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception) when (exception is IOException or Win32Exception
            or OperationCanceledException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            store.Settings.LauncherExecutable = null;
            store.Save();
            LauncherLog.Append(store.Home, $"Update fallback: {exception}");
            return false;
        }
    }
}
