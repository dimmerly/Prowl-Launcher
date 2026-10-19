using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;

namespace Prowl.Launcher;

/// <summary>Every project's backups in one place: back up a project, restore a backup as a copy, show or delete it.</summary>
internal sealed class BackupsDialog(
    LauncherStore store,
    Controls ui,
    Dialogs dialogs,
    ProjectBackupService backups,
    NotificationCenter notifications
)
{
    private IReadOnlyList<ProjectBackup> _backups = [];
    private bool _pending;
    private string _selected = "";

    /// <summary>
    ///     Opens the dialog once no other modal is open. Context menus and confirmations are modals too, so they
    ///     can't open this directly.
    /// </summary>
    public void Request() => _pending = true;

    public void ShowIfPending()
    {
        if (!_pending || Modal.IsOpen)
        {
            return;
        }

        _pending = false;
        Show();
    }

    private void Show()
    {
        _backups = backups.ListAll();
        float width = Math.Clamp(ui.ScreenSize.X - 48, 25, 900);
        float height = Math.Clamp(ui.ScreenSize.Y - 300, 1, 460);
        Origami.Modal(Loc.Get("launcher.backups.title"))
            .Width(width)
            .CenteredContent(p => DrawBackups(p, width - 24, height))
            .Button(Loc.Get("launcher.backups.open_folder"), () =>
            {
                Directory.CreateDirectory(store.BackupsPath);
                ProcessHelper.OpenInShell(store.BackupsPath);
            })
            .Button(Loc.Get("launcher.common.close"), Modal.Pop, OrigamiVariant.Primary)
            .Show();
    }

    /// <summary>Makes a manual backup, which is never pruned automatically.</summary>
    public async Task BackUpAsync(Project project, CancellationToken token)
    {
        ui.Operations.Title = Loc.Get("launcher.backups.creating", new { project = project.Name });
        ui.Operations.ImmediateProgress = true;
        ProjectBackup backup =
            await backups.CreateAsync(project, BackupReason.Manual, null, ui.Operations.Transfer(), token);
        notifications.Notify("launcher.backups.created",
            Loc.Get("launcher.backups.created_description",
                new { project = project.Name, size = Size(backup.ArchiveBytes) }));
    }

    private void DrawBackups(Paper p, float width, float height)
    {
        using (p.Column("backups-view").Size(width, UnitValue.Auto).Gap(14).Enter())
        {
            p.Box("backups-description")
                .Height(UnitValue.Auto)
                .Text(Loc.Get("launcher.settings.backups_description"), ui.Font)
                .FontSize(15).TextColor(ui.Muted).Wrap(TextWrapMode.Wrap).IsNotInteractable();

            Project[] projects = store.Settings.Projects.Where(project => Directory.Exists(project.Path))
                .OrderByDescending(project => project.LastOpened)
                .ToArray();
            if (projects.Length > 0)
            {
                if (!projects.Any(project => project.Path == _selected))
                {
                    _selected = projects[0].Path;
                }

                using (p.Row("backups-create").Height(40).Gap(8).Enter())
                {
                    Controls.AboveParentLayer(p, () => Origami.Dropdown(p, "backups-project", _selected,
                            value => _selected = value,
                            projects.Select(project => project.Path).ToArray())
                        .Display(path => projects.FirstOrDefault(project => project.Path == path)?.Name ?? path)
                        .Width(UnitValue.Stretch())
                        .IsItemEnabled(_ => !ui.Busy)
                        .Show());
                    ui.Button(p, "backups-back-up", "launcher.backups.back_up_now", token =>
                    {
                        Modal.Pop();
                        return BackUpAndReturnAsync(projects.First(project => project.Path == _selected), token);
                    }, true, 130);
                }
            }

            ui.Label(p, "backups-recent", "launcher.backups.recent", 16, ui.Ink, 28, true);
            if (_backups.Count == 0)
            {
                ui.Label(p, "backups-empty", "launcher.backups.empty", 15, ui.Muted, 36);
                return;
            }

            Origami.ScrollView(p, "backups-scroll", width, Math.Min(height, _backups.Count * 86f))
                .SmoothScroll().WheelStep(72).ColSpacing(8)
                .Body(() =>
                {
                    foreach (ProjectBackup backup in _backups)
                    {
                        DrawBackup(p, backup);
                    }
                });
        }
    }

    private void DrawBackup(Paper p, ProjectBackup backup)
    {
        string id = "backup-" + backup.Id;
        using (ui.VersionCard(p, id))
        using (p.Row(id + "row").Height(UnitValue.Auto).Gap(12).AlignItems(LayoutAlignment.Center).Enter())
        {
            using (p.Column(id + "info").Height(UnitValue.Auto).Gap(2).Clip().Enter())
            {
                ui.Label(p, id + "name", backup.ProjectName, 16, ui.Ink, 26, true);
                string version = backup.ProjectVersion ?? "?";
                if (backup.TargetVersion != null)
                {
                    version += " → " + backup.TargetVersion;
                }

                string reason = Loc.Get(backup.Automatic
                    ? "launcher.backups.reason_upgrade"
                    : "launcher.backups.reason_manual");
                ui.Label(p, id + "details",
                    $"{backup.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} ({RelativeDate.FormatElapsed(backup.CreatedAt)}) · {reason} · {version} · {Size(backup.ArchiveBytes)}",
                    13, ui.Muted, 22);
            }

            using (p.Row(id + "buttons").Width(UnitValue.Auto).Height(40).Gap(8).Enter())
            {
                IconButton(p, id + "show", OrigamiIconSet.FolderOpen, "launcher.backups.show",
                    () => ProcessHelper.OpenInShell(Path.GetDirectoryName(backup.Archive)!));
                IconButton(p, id + "delete", OrigamiIconSet.Trash, "launcher.backups.delete",
                    () => ui.Operations.Start(token => DeleteAsync(backup, token), "launcher.backups.delete"));
                ui.Button(p, id + "restore", "launcher.backups.restore_copy", token =>
                {
                    Modal.Pop();
                    return RestoreAsCopyAsync(backup, token);
                }, true, 130);
            }
        }
    }

    private void IconButton(Paper p, string id, IOrigamiIcon icon, string tooltip, Action click) => Origami
        .Button(p, id, "")
        .LeadingIcon(icon)
        .Width(40)
        .Height(40)
        .Rounding(ui.Appearance.Theme.Metrics.Rounding)
        .Tooltip(Loc.Get(tooltip))
        .Disabled(ui.Busy)
        .OnClick(click)
        .Show();

    private async Task BackUpAndReturnAsync(Project project, CancellationToken token)
    {
        await BackUpAsync(project, token);
        Request();
    }

    private async Task RestoreAsCopyAsync(ProjectBackup backup, CancellationToken token)
    {
        ui.Operations.Title = Loc.Get("launcher.backups.restoring", new { project = backup.ProjectName });
        ui.Operations.ImmediateProgress = true;
        Project copy = await backups.RestoreAsCopyAsync(backup, ui.Operations.Transfer(), token);
        notifications.Notify("launcher.backups.restored",
            Loc.Get("launcher.backups.restored_description", new { project = copy.Name, path = copy.Path }));
    }

    private async Task DeleteAsync(ProjectBackup backup, CancellationToken token)
    {
        Modal.Pop();
        if (await dialogs.ConfirmAsync("launcher.backups.delete_title", "launcher.backups.delete_description",
                "launcher.backups.delete", token))
        {
            await backups.DeleteAsync(backup);
        }

        Request();
    }

    private static string Size(long bytes) => bytes >= 1073741824
        ? $"{bytes / 1073741824d:F1} GB"
        : $"{bytes / 1048576d:F1} MB";
}
