using System.Text.Json;

using Prowl.Launcher.Theming;
namespace Prowl.Launcher;

/// <summary>Read-only access to the editor's own settings files, which the launcher follows where it can.</summary>
public static class EditorSettings
{
    private static string SettingsPath => Path.Combine(Platform.ProwlDataPath, "EditorSettings.json");

    internal static string RecentProjectsPath => Path.Combine(Platform.ProwlDataPath, "RecentProjects.json");

    public static string? Locale() =>
        ReadProperty("Locale") is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static string ProjectsDirectory()
    {
        if (ReadProperty("DefaultProjectsPath") is { ValueKind: JsonValueKind.String } value
            && value.GetString() is {} path
            && !string.IsNullOrWhiteSpace(path)
            && Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProwlProjects");
    }

    /// <summary>The editor's current theme, or null when it has none the launcher can read.</summary>
    internal static EditorThemeData? Theme()
    {
        try
        {
            EditorThemeData? data = ReadProperty("Theme")?.Deserialize<EditorThemeData>();
            data?.InitRamps();
            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? ReadProperty(string name)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return json.RootElement.TryGetProperty(name, out JsonElement value) ? value.Clone() : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
