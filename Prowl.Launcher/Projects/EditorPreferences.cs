using System.Text.Json;

namespace Prowl.Launcher;

public static class EditorPreferences
{
    public static string? Locale()
    {
        string settings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Prowl",
            "EditorSettings.json"
        );
        return LauncherStore.ReadJson<LanguagePreference>(settings)?.Locale;
    }

    private sealed class LanguagePreference
    {
        public string? Locale
        {
            get;
            set;
        }
    }

    public static string ProjectsDirectory()
    {
        string settings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Prowl",
            "EditorSettings.json"
        );
        try
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(settings));
            if (json.RootElement.TryGetProperty("DefaultProjectsPath", out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is {} path && !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
            {
                return Path.GetFullPath(path);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProwlProjects");
    }
}
