using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private string _search = "";
    private void DrawProjects(Paper p)
    {
        if (_newProjectPage)
        {
            DrawNewProject(p);
            return;
        }

        Label(p, "projects-title", "launcher.projects.title", 18, Ink, 30, true);
        Project[] projects = store.Settings.Projects.Where(project => (project.Name + project.Path).Contains(_search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(project => project.Favorite)
            .ThenByDescending(project => project.LastOpened)
            .ToArray();
        if (projects.Length == 0)
        {
            using (Card(p, "empty"))
            {
                Label(
                    p,
                    "empty-title",
                    _search.Length > 0 ? "launcher.projects.no_matches" : "launcher.projects.empty_title",
                    23,
                    Ink,
                    42,
                    true
                );
                Label(
                    p,
                    "empty-help",
                    "launcher.projects.empty_description",
                    14,
                    Muted,
                    30
                );
            }
        }

        foreach (Project project in projects)
        {
            string id = "project-" + project.Path;
            bool folderExists = Directory.Exists(project.Path);
            bool wide = IsWideLayout(p);
            using (VersionCard(p, id))
            {
                Origami.RightClickMenu(
                    p,
                    id + "menu",
                    menu =>
                    {
                        if (folderExists)
                        {
                            menu.Item(Loc.Get("launcher.projects.show_in_folder"), () => ProcessHelper.OpenInShell(project.Path));
                        }

                        menu.Item(Loc.Get("launcher.projects.copy_path"), () => p.SetClipboard(project.Path));
                        menu.Item(
                            Loc.Get(project.Favorite ? "launcher.projects.unfavourite" : "launcher.projects.favourite"),
                            () =>
                            {
                                project.Favorite = !project.Favorite;
                                store.Save();
                            }
                        );
                        menu.Item(
                            Loc.Get("launcher.projects.remove"),
                            () =>
                            {
                                store.Settings.Projects.Remove(project);
                                store.Save();
                            }
                        );
                    }
                );
                using ((wide ? p.Row(id + "row") : p.Column(id + "row")).Height(UnitValue.Auto)
                    .Gap(12)
                    .AlignItems(LayoutAlignment.Center)
                    .Enter())
                {
                    using (p.Column(id + "info")
                        .Height(UnitValue.Auto)
                        .Gap(2)
                        .Clip()
                        .Enter())
                    {
                        Label(p, id + "name", project.Name, 19, Ink, 28, true);
                        ElementBuilder path = p.Box(id + "path")
                            .Height(24)
                            .Text(project.Path, _font)
                            .FontSize(16)
                            .TextColor(Muted)
                            .Alignment(TextAlignment.MiddleLeft);
                        if (folderExists)
                        {
                            path.Cursor(PaperCursor.Pointer)
                                .Tooltip(Loc.Get("launcher.projects.show_in_folder"))
                                .OnClick(_ =>
                                {
                                    if (Directory.Exists(project.Path))
                                    {
                                        ProcessHelper.OpenInShell(project.Path);
                                    }
                                });
                            path.Hovered.TextColor(_appearance.Accent);
                        }
                        else
                        {
                            path.IsNotInteractable();
                        }
                        if (project.LastOpened != default)
                        {
                            Label(p, id + "last-opened", RelativeDate.FormatElapsed(project.LastOpened), 12, Muted, 22);
                        }

                        if (!folderExists)
                        {
                            Label(p, id + "missing", "launcher.projects.folder_unavailable", 12, _appearance.Theme.Red.C500, 24);
                        }
                    }

                    using (p.Column(id + "actions")
                        .Width(wide ? UnitValue.Pixels(248) : UnitValue.Stretch())
                        .Height(UnitValue.Auto)
                        .Gap(8)
                        .Enter())
                    {
                        using (p.Row(id + "buttons")
                            .Height(40)
                            .Gap(8)
                            .JustifyContent(LayoutJustification.End)
                            .Enter())
                        {
                            Button(p, id + "remove", "launcher.projects.remove", token => RemoveProjectAsync(project, token), width: 90);
                            Button(p, id + "open", "launcher.projects.open", token => OpenProjectAsync(project, token), true, 90);
                        }

                        string[] keys = _installed.Select(e => e.Key).ToArray();
                        Origami.Dropdown(
                                p,
                                id + "editor",
                                project.EditorKey ?? "",
                                value =>
                                {
                                    project.EditorKey = value;
                                    store.Save();
                                },
                                keys
                            )
                            .Display(key => _installed.FirstOrDefault(e => e.Key == key)?.ToString() ?? Loc.Get("launcher.projects.choose_editor"))
                            .Width(UnitValue.Stretch())
                            .IsItemEnabled(_ => !Busy)
                            .Show();
                    }
                }
            }
        }
    }

    private async Task AddProjectAsync(CancellationToken token)
    {
        if (await PickFolderAsync(EditorSettings.ProjectsDirectory(), token) is {} path)
        {
            Notify("launcher.projects.added", store.AddProject(path).Name);
        }
    }

    private Task RemoveProjectAsync(Project project, CancellationToken token)
    {
        store.Settings.Projects.Remove(project);
        store.Save();
        Notify("launcher.projects.removed", "launcher.projects.files_unchanged", ToastType.Info);
        return Task.CompletedTask;
    }

    private Task OpenProjectAsync(Project project, CancellationToken token)
    {
        InstalledEditor editor = _installed.FirstOrDefault(editor => editor.Key == project.EditorKey)
                                 ?? throw new InvalidOperationException(Loc.Get("launcher.errors.choose_installed_editor"));
        return LaunchAsync(editor, project, token);
    }
}
