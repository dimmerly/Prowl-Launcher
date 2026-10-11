namespace Prowl.Launcher;

/// <summary>Appends diagnostics to launcher.log in the launcher home. Logging never throws.</summary>
static class LauncherLog
{
    internal static void Append(string home, string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(home, "launcher.log"), $"{DateTimeOffset.UtcNow:o} {message}\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static void Append(string home, Exception exception) => Append(home, exception.ToString());
}
