using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>
///     The launcher's home folder: settings shared safely between launcher processes, the project list and
///     the installed editors.
/// </summary>
public sealed class LauncherStore
{

    public string Home
    {
        get;
    }
    public Settings Settings
    {
        get;
    }
    public string SettingsPath => Path.Combine(Home, "settings.json");
    public string WorkPath => Path.Combine(Home, "Work");

    // The default home shares its Versions folder with the editor; custom homes stay self-contained.
    public string VersionsPath => string.Equals(Home, Path.GetFullPath(Platform.DefaultHome), FileSystemHelper.PathComparison)
        ? Platform.DefaultVersionsPath
        : Path.Combine(Home, "Versions");
    private JsonObject _savedSettings;

    public LauncherStore(string? home = null)
    {
        Home = Path.GetFullPath(home ?? Environment.GetEnvironmentVariable(Constants.Storage.HomeEnvironment) ?? Platform.DefaultHome);
        Directory.CreateDirectory(Home);
        using FileStream writeLock = AcquireSettingsLock();
        Settings = LoadSettings();
        _savedSettings = Snapshot(Settings);
    }

    #region Settings

    /// <summary>
    ///     Saves this instance's edits since its last load or save, merged over whatever other launcher
    ///     processes have written in the meantime.
    /// </summary>
    public void Save()
    {
        using FileStream writeLock = AcquireSettingsLock();
        NormalizeSettings(Settings);
        JsonObject local = Snapshot(Settings);
        JsonObject merged = Snapshot(LoadSettings());
        MergeProperties(_savedSettings, local, merged, nameof( Settings.Projects ), nameof( Settings.DismissedLauncherReleases ));
        MergeProperties(
            (JsonObject)_savedSettings[nameof( Settings.DismissedLauncherReleases )]!,
            (JsonObject)local[nameof( Settings.DismissedLauncherReleases )]!,
            (JsonObject)merged[nameof( Settings.DismissedLauncherReleases )]!);
        merged[nameof( Settings.Projects )] = MergeProjects(
            (JsonArray)_savedSettings[nameof( Settings.Projects )]!,
            (JsonArray)local[nameof( Settings.Projects )]!,
            (JsonArray)merged[nameof( Settings.Projects )]!);

        Settings saved = merged.Deserialize<Settings>(JsonFile.Options)!;
        JsonFile.Write(SettingsPath, saved);
        JsonFile.Write(SettingsPath + ".bak", saved);
        CopyIntoCurrentSettings(saved);
        _savedSettings = Snapshot(saved);
    }

    /// <summary>
    ///     Updates <see cref="Settings" /> in place. Project objects are kept alive because UI callbacks may
    ///     hold references to them.
    /// </summary>
    private void CopyIntoCurrentSettings(Settings saved)
    {
        Dictionary<string, Project> existing = Settings.Projects.ToDictionary(p => ProjectKey(p.Path));
        foreach (Project project in saved.Projects)
        {
            if (existing.TryGetValue(ProjectKey(project.Path), out Project? current))
            {
                CopyProperties(project, current);
            }
        }
        saved.Projects = saved.Projects.Select(p => existing.GetValueOrDefault(ProjectKey(p.Path)) ?? p).ToList();
        CopyProperties(saved, Settings);
    }

    private static void CopyProperties<T>(T source, T target)
    {
        foreach (PropertyInfo property in typeof( T ).GetProperties())
        {
            property.SetValue(target, property.GetValue(source));
        }
    }

    /// <summary>Serializes installs, uninstalls, updates and cleanup across launcher processes.</summary>
    public FileStream AcquireOperationLock()
    {
        try
        {
            return new FileStream(Path.Combine(Home, "operations.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new IOException(Loc.Get("launcher.errors.operation_in_progress"), error);
        }
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

    /// <summary>
    ///     Loads settings.json, falling back to its backup. Damaged files are kept aside rather than deleted.
    ///     Called under the settings lock so another launcher cannot save during recovery.
    /// </summary>
    private Settings LoadSettings()
    {
        string path = SettingsPath;
        Settings? settings = ReadSettings(path);
        if (settings != null)
        {
            JsonObject original = Snapshot(settings);
            NormalizeSettings(settings);
            if (!JsonNode.DeepEquals(original, Snapshot(settings)))
            {
                PreserveDamagedSettings(path);
                JsonFile.Write(path, settings);
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
        if (!restored && File.Exists(backup))
        {
            PreserveDamagedSettings(backup);
        }
        settings ??= new Settings();
        NormalizeSettings(settings);
        if (restored)
        {
            JsonFile.Write(path, settings);
        }
        return settings;
    }

    private static Settings? ReadSettings(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonFile.Options);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }

    private static void PreserveDamagedSettings(string path) =>
        File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));

    /// <summary>Repairs hand-edited or older settings: drops invalid projects, merges duplicates and validates repositories.</summary>
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
                key = ProjectKey(project.Path);
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
                return GitHubRepository.Normalize(value);
            }
            catch (InvalidDataException)
            {
                return fallback;
            }
        }
    }

    private static JsonObject Snapshot(Settings settings) => (JsonObject)JsonSerializer.SerializeToNode(settings, JsonFile.Options)!;

    /// <summary>
    ///     Copies properties that changed between <paramref name="baseline" /> and <paramref name="local" /> into
    ///     <paramref name="target" />.
    /// </summary>
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

    /// <summary>Three-way merge of the project list, keyed by project path.</summary>
    private static JsonArray MergeProjects(JsonArray baseline, JsonArray local, JsonArray disk)
    {
        Dictionary<string, JsonObject> before = Index(baseline);
        Dictionary<string, JsonObject> edits = Index(local);
        Dictionary<string, JsonObject> merged = Index(disk);
        foreach (string removed in before.Keys.Except(edits.Keys))
        {
            merged.Remove(removed);
        }
        foreach ((string path, JsonObject project) in edits)
        {
            if (before.TryGetValue(path, out JsonObject? original) && JsonNode.DeepEquals(original, project))
            {
                // Unchanged here; keep whatever the other process did, including removing it.
                continue;
            }
            if (original != null && merged.TryGetValue(path, out JsonObject? target))
            {
                MergeProperties(original, project, target);
            }
            else
            {
                merged[path] = (JsonObject)project.DeepClone();
            }
        }
        return new JsonArray(merged.Values.Select(p => (JsonNode)p.DeepClone()).ToArray());

        static Dictionary<string, JsonObject> Index(JsonArray projects) => projects.Cast<JsonObject>()
            .ToDictionary(p => ProjectKey(p[nameof( Project.Path )]!.GetValue<string>()));
    }

    #endregion

    #region Editors

    public string InstallPath(InstalledEditor editor)
    {
        bool valid = (editor.ReleaseId > 0 || editor.IsMain)
                     && Constants.Storage.SupportedPlatforms.Contains(editor.Platform)
                     && !string.IsNullOrEmpty(editor.Tag)
                     && Regex.IsMatch(editor.Tag, @"\A[A-Za-z0-9][A-Za-z0-9.+-]*\z");
        if (!valid)
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_installation"));
        }

        return FileSystemHelper.SafeChildPath(VersionsPath, editor.Key);
    }

    public string ExecutablePath(InstalledEditor editor) =>
        FileSystemHelper.SafeChildPath(InstallPath(editor), editor.ExecutableRelativePath);

    /// <summary>Editors whose installation.json matches the folder they were found in, newest first.</summary>
    public IReadOnlyList<InstalledEditor> InstalledEditors()
    {
        if (!Directory.Exists(VersionsPath))
        {
            return [];
        }

        List<InstalledEditor> result = [];
        foreach (string folder in Directory.EnumerateDirectories(VersionsPath))
        {
            if (FileSystemHelper.IsLink(folder)
                || JsonFile.Read<InstalledEditor>(Path.Combine(folder, "installation.json")) is not {} editor)
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

    #endregion

    #region Projects

    public Project AddProject(string path) => AddProject(path, true);

    private Project AddProject(string path, bool save)
    {
        bool isMarker = File.Exists(path) && path.EndsWith(".prowl", StringComparison.OrdinalIgnoreCase);
        string root = Path.GetFullPath(isMarker ? Path.GetDirectoryName(path)! : path);
        if (!Directory.Exists(Path.Combine(root, "Assets")))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_project_folder"));
        }

        Project? existing = Settings.Projects.FirstOrDefault(p => FileSystemHelper.PathsEqual(p.Path, root));
        if (existing != null)
        {
            return existing;
        }

        (string name, string? version) = ReadProjectMarker(root);
        // Match both historical preview-4 tags and the editor's newer preview.4 version format.
        InstalledEditor? matching = InstalledEditors().FirstOrDefault(e =>
            e.Repository.Equals(Settings.ProwlRepository, StringComparison.OrdinalIgnoreCase)
            && VersionHelper.EquivalentEditorTags(e.Tag, version));
        Project project = new()
        {
            Name = name, Path = root, EditorKey = matching?.Key ?? Settings.DefaultEditorKey
        };
        Settings.Projects.Add(project);
        if (save)
        {
            Save();
        }
        return project;
    }

    /// <summary>The engine version a project was last saved with, or null when it has none or can't be read.</summary>
    public static string? ReadProjectVersion(string root)
    {
        try
        {
            return Directory.Exists(root) ? ReadProjectMarker(root).Version : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Reads the name and editor version from a project's .prowl file, defaulting to the folder name.</summary>
    private static (string Name, string? Version) ReadProjectMarker(string root)
    {
        string name = Path.GetFileName(root);
        string? marker = Directory.EnumerateFiles(root, "*.prowl").FirstOrDefault();
        if (marker == null)
        {
            return (name, null);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(marker));
        JsonElement json = document.RootElement;
        string? version = json.TryGetProperty("version", out JsonElement v) ? v.GetString() : null;
        if (json.TryGetProperty("name", out JsonElement n))
        {
            name = n.GetString() ?? name;
        }
        return (name, version);
    }

    /// <summary>Imports the editor's recent projects once, then keeps favourites and open times in sync.</summary>
    public void ImportRecentProjects(string? recentProjectsPath = null)
    {
        if (Settings.RecentProjectsImported && Settings.RecentProjectMetadataImported)
        {
            return;
        }

        List<Project>? entries = JsonFile.Read<List<Project>>(recentProjectsPath ?? EditorSettings.RecentProjectsPath);
        foreach (Project entry in entries ?? [])
        {
            try
            {
                Project? project = Settings.RecentProjectsImported
                    ? Settings.Projects.FirstOrDefault(p => FileSystemHelper.PathsEqual(p.Path, entry.Path))
                    : AddProject(entry.Path, false);
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

    private static string ProjectKey(string path) => OperatingSystem.IsWindows()
        ? Path.GetFullPath(path).ToUpperInvariant()
        : Path.GetFullPath(path);

    #endregion
}
