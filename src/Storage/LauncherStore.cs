using Prowl.Rosetta;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Prowl.Launcher;

public sealed class LauncherStore
{
    private JsonObject _savedSettings;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    public string Home
    {
        get;
    }
    public string VersionsPath => string.Equals(Home, Path.GetFullPath(Platform.DefaultHome),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        ? Platform.DefaultVersionsPath
        : Path.Combine(Home, "Versions");
    public string WorkPath => Path.Combine(Home, "Work");
    public Settings Settings
    {
        get;
    }

    public LauncherStore(string? home = null)
    {
        Home = Path.GetFullPath(home ?? Environment.GetEnvironmentVariable(Constants.Storage.HomeEnvironment) ?? Platform.DefaultHome);
        Directory.CreateDirectory(Home);
        using FileStream writeLock = AcquireSettingsLock();
        Settings = LoadSettings();
        _savedSettings = Snapshot(Settings);
    }

    public void Save()
    {
        // Merge only this instance's edits while holding a cross-process write lock.
        using FileStream writeLock = AcquireSettingsLock();
        string path = Path.Combine(Home, "settings.json");
        NormalizeSettings(Settings);
        JsonObject local = Snapshot(Settings);
        JsonObject merged = Snapshot(LoadSettings());
        MergeProperties(_savedSettings, local, merged, "Projects", "DismissedLauncherReleases");
        MergeProperties((JsonObject)_savedSettings["DismissedLauncherReleases"]!,
            (JsonObject)local["DismissedLauncherReleases"]!, (JsonObject)merged["DismissedLauncherReleases"]!);
        merged["Projects"] = MergeProjects((JsonArray)_savedSettings["Projects"]!,
            (JsonArray)local["Projects"]!, (JsonArray)merged["Projects"]!);
        Settings saved = merged.Deserialize<Settings>(JsonOptions)!;
        WriteJson(path, saved);
        WriteJson(path + ".bak", saved);
        // Keep project objects alive because UI callbacks may hold references to them.
        Dictionary<string, Project> existing = Settings.Projects.ToDictionary(p => ProjectPath(p.Path));
        foreach (Project project in saved.Projects)
        {
            if (!existing.TryGetValue(ProjectPath(project.Path), out Project? current))
            {
                continue;
            }
            foreach (PropertyInfo property in typeof( Project ).GetProperties())
            {
                property.SetValue(current, property.GetValue(project));
            }
        }
        saved.Projects = saved.Projects.Select(p => existing.GetValueOrDefault(ProjectPath(p.Path)) ?? p).ToList();
        foreach (PropertyInfo property in typeof( Settings ).GetProperties())
        {
            property.SetValue(Settings, property.GetValue(saved));
        }
        _savedSettings = Snapshot(saved);
    }

    private FileStream AcquireSettingsLock()
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(Path.Combine(Home, "settings.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (clock.Elapsed < Constants.Storage.SettingsLockTimeout)
            {
                Thread.Sleep(Constants.Storage.SettingsLockRetryMilliseconds);
            }
        }
    }

    // Called under the settings lock so another launcher cannot save during recovery.
    private Settings LoadSettings()
    {
        string path = Path.Combine(Home, "settings.json");
        Settings? settings = ReadSettings(path);
        if (settings != null)
        {
            JsonObject original = Snapshot(settings);
            NormalizeSettings(settings);
            if (!JsonNode.DeepEquals(original, Snapshot(settings)))
            {
                PreserveDamagedSettings(path);
                WriteJson(path, settings);
            }
            return settings;
        }

        if (File.Exists(path))
        {
            PreserveDamagedSettings(path);
        }
        string backup = path + ".bak";
        settings = ReadSettings(backup);
        bool restored = settings != null;
        if (settings == null && File.Exists(backup))
        {
            PreserveDamagedSettings(backup);
        }
        settings ??= new Settings();
        NormalizeSettings(settings);
        if (restored)
        {
            WriteJson(path, settings);
        }
        return settings;
    }

    private static Settings? ReadSettings(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }

    private static void PreserveDamagedSettings(string path) =>
        File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));

    private static void NormalizeSettings(Settings settings)
    {
        settings.DismissedLauncherReleases ??= [];
        Dictionary<string, Project> projects = [];
        foreach (Project? project in settings.Projects ?? [])
        {
            if (project == null || string.IsNullOrWhiteSpace(project.Path))
            {
                continue;
            }
            string key;
            try
            {
                key = ProjectPath(project.Path);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            project.Name ??= Path.GetFileName(project.Path);
            if (projects.TryGetValue(key, out Project? existing))
            {
                existing.Favorite |= project.Favorite;
                existing.EditorKey ??= project.EditorKey;
                if (project.LastOpened > existing.LastOpened)
                {
                    existing.LastOpened = project.LastOpened;
                }
            }
            else
            {
                projects.Add(key, project);
            }
        }
        settings.Projects = projects.Values.ToList();
        settings.ProwlRepository = ValidRepository(settings.ProwlRepository, Constants.Defaults.ProwlRepository);
        settings.LauncherRepository = ValidRepository(settings.LauncherRepository, Constants.Defaults.LauncherRepository);

        static string ValidRepository(string? value, string fallback)
        {
            if (value == null)
            {
                return fallback;
            }
            try
            {
                return GitHubRepositoryHelper.Normalize(value);
            }
            catch (InvalidDataException)
            {
                return fallback;
            }
        }
    }

    private static JsonObject Snapshot(Settings settings) => (JsonObject)JsonSerializer.SerializeToNode(settings, JsonOptions)!;
    private static string ProjectPath(string path) => OperatingSystem.IsWindows()
        ? Path.GetFullPath(path).ToUpperInvariant() : Path.GetFullPath(path);

    private static void MergeProperties(JsonObject baseline, JsonObject local, JsonObject target, params string[] skip)
    {
        foreach ((string key, JsonNode? value) in local)
        {
            if (!skip.Contains(key) && !JsonNode.DeepEquals(baseline[key], value))
            {
                target[key] = value?.DeepClone();
            }
        }
    }

    private static JsonArray MergeProjects(JsonArray baseline, JsonArray local, JsonArray disk)
    {
        Dictionary<string, JsonObject> Index(JsonArray projects) => projects.Cast<JsonObject>()
            .ToDictionary(p => ProjectPath(p["Path"]!.GetValue<string>()));
        Dictionary<string, JsonObject> before = Index(baseline);
        Dictionary<string, JsonObject> edits = Index(local);
        Dictionary<string, JsonObject> merged = Index(disk);
        foreach (string removed in before.Keys.Except(edits.Keys))
        {
            merged.Remove(removed);
        }
        foreach ((string path, JsonObject project) in edits)
        {
            if (before.TryGetValue(path, out JsonObject? original))
            {
                if (JsonNode.DeepEquals(original, project))
                {
                    continue;
                }
                if (!merged.TryGetValue(path, out JsonObject? target))
                {
                    merged[path] = (JsonObject)project.DeepClone();
                }
                else
                {
                    MergeProperties(original, project, target);
                }
            }
            else
            {
                merged[path] = (JsonObject)project.DeepClone();
            }
        }
        return new JsonArray(merged.Values.Select(p => (JsonNode)p.DeepClone()).ToArray());
    }
    public static T? ReadJson<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return default;
        }
    }

    public static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
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

    public string InstallPath(InstalledEditor editor)
    {
        if ((editor.ReleaseId <= 0 && !editor.IsMain) || !Constants.Storage.SupportedPlatforms.Contains(editor.Platform)
                                  || string.IsNullOrEmpty(editor.Tag) || !Regex.IsMatch(editor.Tag, @"\A[A-Za-z0-9][A-Za-z0-9.+-]*\z"))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_installation"));
        }

        return SafeChildPath(VersionsPath, editor.Key);
    }

    public IReadOnlyList<InstalledEditor> InstalledEditors()
    {
        if (!Directory.Exists(VersionsPath))
        {
            return [];
        }

        List<InstalledEditor> result = [];
        foreach (string folder in Directory.EnumerateDirectories(VersionsPath))
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            InstalledEditor? editor = ReadJson<InstalledEditor>(Path.Combine(folder, "installation.json"));
            if (editor == null)
            {
                continue;
            }

            try
            {
                if (Path.GetFullPath(folder) == InstallPath(editor))
                {
                    result.Add(editor);
                }
            }
            catch (InvalidDataException)
            {
            }
        }

        return result.OrderByDescending(e => e.InstalledAt).ToArray();
    }

    public string ExecutablePath(InstalledEditor editor) => SafeChildPath(InstallPath(editor), editor.ExecutableRelativePath);
    public static string SafeChildPath(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.IsPathRooted(relative) || !fullPath.StartsWith(fullRoot, comparison))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.path_outside_folder"));
        }

        return fullPath;
    }

    public Project AddProject(string path)
    {
        string root = Path.GetFullPath(File.Exists(path) && path.EndsWith(".prowl", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path)! : path);
        if (!Directory.Exists(Path.Combine(root, "Assets")))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_project_folder"));
        }

        Project? existing = Settings.Projects.FirstOrDefault(p => PathsEqual(p.Path, root));
        if (existing != null)
        {
            return existing;
        }

        string name = Path.GetFileName(root);
        string? marker = Directory.EnumerateFiles(root, "*.prowl").FirstOrDefault();
        string? version = null;
        if (marker != null)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(marker));
            if (doc.RootElement.TryGetProperty("name", out JsonElement n))
            {
                name = n.GetString() ?? name;
            }

            if (doc.RootElement.TryGetProperty("version", out JsonElement v))
            {
                version = v.GetString();
            }
        }

        // Match both historical preview-4 tags and the editor's newer preview.4 version format.
        InstalledEditor? matching = InstalledEditors()
            .FirstOrDefault(e => e.Repository.Equals(Settings.ProwlRepository, StringComparison.OrdinalIgnoreCase)
                                 && VersionHelper.EquivalentEditorTags(e.Tag, version));
        Project project = new()
        {
            Name = name, Path = root, EditorKey = matching?.Key ?? Settings.DefaultEditorKey
        };
        Settings.Projects.Add(project);
        Save();
        return project;
    }

    public void ImportRecentProjects(string? recentProjectsPath = null)
    {
        if (Settings.RecentProjectsImported && Settings.RecentProjectMetadataImported)
        {
            return;
        }

        string path = recentProjectsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Prowl",
            "RecentProjects.json"
        );
        List<Project>? entries = ReadJson<List<Project>>(path);
        foreach (Project entry in entries ?? [])
        {
            try
            {
                Project? project = Settings.RecentProjectsImported ? Settings.Projects.FirstOrDefault(p => PathsEqual(p.Path, entry.Path)) : AddProject(entry.Path);
                if (project == null)
                {
                    continue;
                }

                project.Favorite |= entry.Favorite;
                if (entry.LastOpened > project.LastOpened)
                {
                    project.LastOpened = entry.LastOpened;
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException)
            {
            }
        }

        Settings.RecentProjectsImported = true;
        Settings.RecentProjectMetadataImported = true;
        Save();
    }

    public static bool PathsEqual(string a, string b) => string.Equals(
        Path.GetFullPath(a),
        Path.GetFullPath(b),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
    );
}
