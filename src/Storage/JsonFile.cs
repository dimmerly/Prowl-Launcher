using System.Text.Json;

namespace Prowl.Launcher;

/// <summary>Tolerant reads and atomic writes for the launcher's JSON files.</summary>
internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Returns <c>default</c> when the file is missing, unreadable or malformed.</summary>
    public static T? Read<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return default;
        }
    }

    /// <summary>Writes through a temporary file so readers never observe a partial document.</summary>
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
