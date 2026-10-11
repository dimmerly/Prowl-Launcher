using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;
namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private string? _newProjectDirectory;
    private string? _newProjectEditorKey;
    private string _newProjectName = "";
    private bool _newProjectPage = previews?.NewProject == true;
    private void DrawNewProject(Paper p)
    {
        _newProjectDirectory ??= EditorSettings.ProjectsDirectory();
        _newProjectEditorKey ??= store.Settings.DefaultEditorKey ?? _installed.FirstOrDefault()?.Key;
        bool wide = IsWideLayout(p);
        using ((wide ? p.Row("new-project-form") : p.Column("new-project-form"))
            .Height(UnitValue.Auto)
            .Gap(18)
            .Enter())
        {
            using (p.Column("project-templates")
                .Height(UnitValue.Auto)
                .Gap(12)
                .Enter())
            {
                Label(p, "templates-title", "launcher.projects.templates", 19, Ink, 28, true);
                using (p.Column("blank-template")
                    .Height(UnitValue.Auto)
                    .Padding(20)
                    .Gap(10)
                    .BackgroundColor(_appearance.Theme.Selected)
                    .BorderColor(_appearance.Accent)
                    .BorderWidth(1)
                    .Rounded(_appearance.Theme.Metrics.ContainerRounding)
                    .Enter())
                {
                    p.Box("blank-icon")
                        .Size(48, 48)
                        .Rounded(_appearance.Theme.Metrics.ContainerRounding)
                        .BackgroundColor(_appearance.Accent)
                        .IsNotInteractable()
                        .Icon(p, OrigamiIconSet.Document, Color.White, size: 26);
                    Label(p, "blank-title", Loc.Get("launcher.projects.blank_name"), 18, Ink, 28, true);
                    p.Box("blank-description")
                        .Height(UnitValue.Auto)
                        .Text(Loc.Get("launcher.projects.blank_description"), _font)
                        .FontSize(16)
                        .TextColor(Muted)
                        .Wrap(TextWrapMode.Wrap)
                        .IsNotInteractable();
                }
            }

            using (p.Column("new-project-config")
                .Width(wide ? UnitValue.Pixels(380) : UnitValue.Stretch())
                .Height(UnitValue.Auto)
                .Padding(20)
                .Gap(8)
                .BackgroundColor(_appearance.Card)
                .BorderColor(_appearance.Theme.BorderSoft)
                .BorderWidth(1)
                .Rounded(_appearance.Theme.Metrics.ContainerRounding)
                .Enter())
            {
                Label(p, "configure-title", Loc.Get("launcher.projects.configure"), 19, Ink, 28, true);
                Label(p, "new-name-label", Loc.Get("launcher.projects.name"), 14, Muted, 24);
                Origami.TextField(p, "new-project-name", _newProjectName, value => _newProjectName = value)
                    .Height(40)
                    .Show();
                Label(p, "new-directory-label", Loc.Get("launcher.projects.path"), 14, Muted, 24);
                using (p.Row("new-location-row")
                    .Height(40)
                    .Gap(8)
                    .Enter())
                {
                    Origami.TextField(p, "new-project-directory", _newProjectDirectory, value => _newProjectDirectory = value)
                        .Height(40)
                        .Show();
                    Origami.Button(p, "browse-new-location", "")
                        .Width(40)
                        .Height(40)
                        .LeadingIcon(OrigamiIconSet.FolderOpen)
                        .Tooltip(Loc.Get("launcher.common.browse"))
                        .Disabled(Busy)
                        .OnClick(() => Start(PickNewProjectDirectoryAsync, "launcher.common.browse"))
                        .Show();
                }

                Label(p, "new-editor-label", "launcher.projects.editor_version", 14, Muted, 24);
                Origami.Dropdown(
                        p,
                        "new-editor",
                        _newProjectEditorKey ?? "",
                        value => _newProjectEditorKey = value,
                        _installed.Select(e => e.Key).ToArray()
                    )
                    .Display(key => _installed.FirstOrDefault(e => e.Key == key)?.ToString() ?? Loc.Get("launcher.projects.choose_editor"))
                    .Width(UnitValue.Stretch())
                    .IsItemEnabled(_ => !Busy)
                    .Show();

                if (_installed.Count == 0)
                {
                    p.Box("need-editor")
                        .Height(UnitValue.Auto)
                        .Text(Loc.Get("launcher.projects.install_editor_first"), _font)
                        .FontSize(16)
                        .TextColor(Muted)
                        .Wrap(TextWrapMode.Wrap)
                        .IsNotInteractable();
                }
            }
        }

        using (p.Row("create-project-actions")
            .Height(40)
            .Gap(8)
            .JustifyContent(LayoutJustification.End)
            .Enter())
        {
            Button(
                p,
                "cancel-new-project",
                "launcher.common.cancel",
                _ =>
                {
                    _newProjectPage = false;
                    return Task.CompletedTask;
                },
                width: 100
            );
            Button(p, "create-project", "launcher.projects.create_and_open", CreateProjectAsync, true, 155);
        }
    }

    private Task ToggleProjectPageAsync(CancellationToken token)
    {
        _newProjectPage = !_newProjectPage;
        return Task.CompletedTask;
    }

    private async Task PickNewProjectDirectoryAsync(CancellationToken token)
    {
        if (await PickFolderAsync(_newProjectDirectory ?? EditorSettings.ProjectsDirectory(), token) is {} selected)
        {
            _newProjectDirectory = selected;
        }
    }

    /// <summary>Creates a blank project for the chosen editor, adds it to the list and opens it.</summary>
    private async Task CreateProjectAsync(CancellationToken token)
    {
        InstalledEditor editor = _installed.FirstOrDefault(e => e.Key == _newProjectEditorKey)
                                 ?? throw new InvalidOperationException(Loc.Get("launcher.errors.choose_installed_editor"));
        // Validate the installation before creating anything on disk.
        _ = _installer.LaunchInfo(editor);
        string parent = _newProjectDirectory ?? EditorSettings.ProjectsDirectory();
        string name = _newProjectName;
        string path = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return ProjectTemplates.CreateBlank(parent, name, editor);
        }, token);

        Project project = store.AddProject(path);
        project.EditorKey = editor.Key;
        store.Save();
        _newProjectPage = false;
        Notify("launcher.projects.created", project.Name);
        await LaunchAsync(editor, project, token);
    }
}
