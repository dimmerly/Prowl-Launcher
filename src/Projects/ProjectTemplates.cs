using Prowl.Rosetta;

using System.Text.RegularExpressions;

namespace Prowl.Launcher;

/// <summary>Creates the editor's blank project layout, stamped for the selected installed editor.</summary>
public static class ProjectTemplates
{
    public static string CreateBlank(string parentDirectory, string name, InstalledEditor editor)
    {
        name = name.Trim();
        bool invalidName = name.Length == 0
                           || name is "." or ".."
                           || name.EndsWith('.')
                           || name.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) >= 0
                           || name.Any(char.IsControl)
                           || Regex.IsMatch(name, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)", RegexOptions.IgnoreCase);
        if (invalidName)
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_project_name"));
        }

        string version = ProjectVersionFor(editor);
        if (!Regex.IsMatch(version, "\\A[0-9]+\\.[0-9]+(?:\\.[0-9]+)?(?:-[A-Za-z]+[.-][0-9]+)?\\z"))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.unknown_project_format"));
        }

        parentDirectory = Path.GetFullPath(parentDirectory);
        string destination = LauncherStore.SafeChildPath(parentDirectory, name);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException(Loc.Get("launcher.errors.project_folder_exists"));
        }

        Directory.CreateDirectory(parentDirectory);
        string staging = Path.Combine(parentDirectory, ".prowl-create-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string folder in new[]
                {
                    "Assets",
                    "Library/cache",
                    "Library/thumbnails",
                    "Library/ScriptAssemblies",
                    "ProjectSettings",
                    "Packages",
                    "Temp",
                    "Logs"
                }
            )
            {
                Directory.CreateDirectory(Path.Combine(staging, folder));
            }

            LauncherStore.WriteJson(
                Path.Combine(staging, name + ".prowl"),
                new
                {
                    name, engine = "Prowl", version, created = DateTimeOffset.UtcNow
                }
            );
            File.WriteAllText(
                Path.Combine(staging, ".gitignore"),
                "Library/\nTemp/\nLogs/\nBackups/\n*.csproj\n*.sln\n*.slnx\n.vs/\nbin/\nobj/\n.DS_Store\n"
            );
            File.WriteAllText(
                Path.Combine(staging, "Directory.Build.props"),
                "<Project>\n  <!-- Add shared NuGet or project references here. Prowl preserves this file. -->\n  <ItemGroup />\n  <!-- Editor-only references belong in this group. -->\n  <ItemGroup Condition=\"$(MSBuildProjectName.EndsWith('.Editor'))\" />\n</Project>\n"
            );
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
        }
    }

    private static string ProjectVersionFor(InstalledEditor editor) => editor.Tag.TrimStart('v')
        .Replace("-preview-", "-preview.")
        .Replace("-alpha-", "-alpha.")
        .Replace("-beta-", "-beta.");
}
