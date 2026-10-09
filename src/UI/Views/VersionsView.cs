using Prowl.Rosetta;

using System.Diagnostics;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private bool _includeEditorPrereleases = Constants.Defaults.EditorPrereleases;

    private void DrawVersions(Paper p)
    {
        using (p.Row("installed-heading").Height(40).Gap(10).AlignItems(LayoutAlignment.Center).Enter())
        {
            Label(p, "installed-title", "launcher.versions.installed_title", 18, Ink, 30, true);
            Button(p, "folder", "launcher.versions.install_folder", OpenInstallFolderAsync, width: 130);
        }

        if (_installed.Count == 0)
        {
            Label(p, "none", "launcher.versions.none_installed", 14, Muted, 30);
        }

        foreach (InstalledEditor editor in _installed)
        {
            string id = "installed-" + editor.Key;
            bool wide = p.ScreenRect.Size.X - Constants.Layout.SidebarWidth - 56 >= 800;
            using (VersionCard(p, id))
            {
                bool showMaintenance = p.IsParentHovered;
                using ((wide ? p.Row(id + "row") : p.Column(id + "row")).Height(UnitValue.Auto)
                    .Gap(12)
                    .AlignItems(LayoutAlignment.Center)
                    .Enter())
                {
                    using (p.Column(id + "info")
                        .Height(UnitValue.Auto)
                        .Gap(2)
                        .Enter())
                    {
                        using (p.Row(id + "title").Height(32).Gap(8).AlignItems(LayoutAlignment.Center).Enter())
                        {
                            p.Box(id + "name")
                                .Width(UnitValue.Auto)
                                .Height(32)
                                .IsNotInteractable()
                                .Text(editor.Tag, _bold)
                                .FontSize(19.8f)
                                .TextColor(Ink)
                                .Alignment(TextAlignment.MiddleLeft);

                            bool isDefault = editor.Key == store.Settings.DefaultEditorKey;
                            string hint = isDefault
                                ? "launcher.versions.default_hint"
                                : "launcher.versions.set_default_hint";

                            Origami.Button(p, id + "default", "")
                                .LeadingIcon(isDefault
                                    ? LauncherIcons.FilledStar.Tinted(System.Drawing.Color.Gold)
                                    : OrigamiIconSet.Star.Tinted(Muted))
                                .Width(28)
                                .Height(28)
                                .Subtle()
                                .Tooltip(Loc.Get(hint))
                                .Disabled(Busy)
                                .OnClick(() => SetDefaultEditor(editor, !isDefault))
                                .Show();

                            if (editor.Repository.Equals(store.Settings.ProwlRepository, StringComparison.OrdinalIgnoreCase)
                                && _releases.FirstOrDefault(release => release.Id == editor.ReleaseId) is {} installedRelease)
                            {
                                DrawChangelogButton(p, id + "notes", installedRelease, true);
                            }

                        }
                        Label(
                            p,
                            id + "meta",
                            Loc.Get("launcher.versions.installed_metadata", new
                            {
                                platform = editor.Platform, date = editor.InstalledAt.LocalDateTime.ToString("d")
                            }),
                            12,
                            Muted,
                            22
                        );
                    }

                    using (p.Row(id + "actions")
                        .Width(wide ? UnitValue.Auto : UnitValue.Stretch())
                        .MaxWidth(wide ? UnitValue.Pixels((float)p.ScreenRect.Size.X - Constants.Layout.SidebarWidth - 56 - 250) : UnitValue.Percentage(100))
                        .Height(UnitValue.Auto)
                        .WrapContent()
                        .Gap(8)
                        .LineGap(8)
                        .JustifyContent(LayoutJustification.End)
                        .Enter())
                    {
                        if (showMaintenance)
                        {
                            Button(p, id + "uninstall", "launcher.versions.uninstall", token => UninstallEditorAsync(editor, token), width: 110);
                            Button(p, id + "repair", "launcher.versions.repair",
                                token => RepairEditorAsync(editor, token), width: 90);
                        }
                        Button(p, id + "launch", "launcher.versions.launch", token => LaunchAsync(editor, null, token), true, 90);
                    }
                }

            }
        }

        using (p.Row("available-heading").Height(40).Gap(10).AlignItems(LayoutAlignment.Center).Enter())
        {
            Label(p, "available-title", "launcher.versions.available_title", 18, Ink, 35, true);

            using (p.Row("include-editor-prereleases-control").Width(UnitValue.Auto).Height(40)
                .Gap(8).AlignItems(LayoutAlignment.Center).Enter())
            {
                p.Box("include-editor-prereleases-label").Width(UnitValue.Auto).Height(24)
                    .Text(Loc.Get("launcher.versions.include_prereleases"), _font)
                    .FontSize(_appearance.Theme.Metrics.FontSize).TextColor(Ink)
                    .Alignment(TextAlignment.MiddleLeft)
                    .Cursor(Busy ? PaperCursor.Default : PaperCursor.Pointer)
                    .OnClick(_ =>
                    {
                        if (!Busy) _includeEditorPrereleases = !_includeEditorPrereleases;
                    });
                Origami.Toggle(p, "include-editor-prereleases", _includeEditorPrereleases, value => _includeEditorPrereleases = value)
                    .NoLabel()
                    .Disabled(Busy).Show();
            }
            Button(p, "refresh", "launcher.versions.refresh", token => RefreshAsync(token), width: 96);
        }

        HashSet<long> installedReleaseIds = _installed
            .Where(e => e.Repository.Equals(store.Settings.ProwlRepository, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.ReleaseId).ToHashSet();
        EditorRelease[] available = _releases.Where(r => !installedReleaseIds.Contains(r.Id) && r.AssetFor(Platform.Identifier) != null && (_includeEditorPrereleases || !r.Preview))
            .ToArray();
        if (available.Length == 0)
        {
            Label(p, "no-releases", "launcher.versions.none_available", 14, Muted, 35);
        }

        foreach (EditorRelease release in available)
        {
            string id = "release-" + release.Id;
            using (VersionCard(p, id))
            {
                using ((p.ScreenRect.Size.X - Constants.Layout.SidebarWidth - 56 < 800 ? p.Column(id + "row") : p.Row(id + "row")).Height(UnitValue.Auto)
                    .AlignItems(LayoutAlignment.Center)
                    .Gap(12)
                    .Enter())
                {
                    using (p.Column(id + "info")
                        .Height(UnitValue.Auto)
                        .Gap(2)
                        .Enter())
                    {
                        Label(p, id + "name", release.Tag, 18, Ink, 28, true);
                        Label(
                            p,
                            id + "meta",
                            $"{Loc.Get(release.Preview ? "launcher.versions.preview" : "launcher.versions.stable")} · {release.Published.LocalDateTime:d} · {release.AssetFor(Platform.Identifier)!.Size / 1048576d:F0} MB",
                            12,
                            Muted,
                            22
                        );
                    }

                    DrawChangelogButton(p, id + "notes", release);
                    Button(p, id + "install", "launcher.versions.install", token => InstallAsync(release, token), true, 90);
                }
            }
        }

    }

    private InstalledEditor DefaultEditor() => _installed.FirstOrDefault(e => e.Key == store.Settings.DefaultEditorKey) ?? _installed.FirstOrDefault() ?? throw new InvalidOperationException(Loc.Get("launcher.errors.install_editor_first"));
    private async Task InstallAsync(EditorRelease release, CancellationToken token, bool repair = false, string? repository = null)
    {
        _operationTitle = Loc.Get(repair ? "launcher.versions.repairing" : "launcher.versions.installing", new
        {
            version = release.Tag
        });
        _immediateProgress = true;
        await _installer.InstallAsync(release, Platform.Identifier, Transfer(), token, repository);
        Notify(repair ? "launcher.versions.repaired" : "launcher.versions.installed", Loc.Get("launcher.versions.ready", new
        {
            version = release.Tag
        }));
    }

    private async Task LaunchAsync(InstalledEditor editor, Project? project, CancellationToken token)
    {
        int sdk = _installer.RequiredSdkMajor(editor);
        if (!await EditorInstallerService.HasSdkAsync(sdk, token))
        {
            if (!await ConfirmAsync(
                    Loc.Get("launcher.sdk.required_title", new
                    {
                        sdk
                    }),
                    Loc.Get("launcher.sdk.required_description", new
                    {
                        sdk
                    }),
                    "launcher.sdk.open_anyway",
                    token,
                    $"https://dotnet.microsoft.com/download/dotnet/{sdk}.0"
                ))
            {
                return;
            }
        }

        using Process process = Process.Start(_installer.LaunchInfo(editor, project?.Path)) ?? throw new InvalidOperationException(Loc.Get("launcher.errors.editor_start_failed"));
        if (project != null)
        {
            project.LastOpened = DateTimeOffset.UtcNow;
            store.Save();
        }

        Notify("launcher.projects.opened", project?.Name ?? editor.Tag);
        if (store.Settings.CloseOnEditorLaunch)
        {
            _closeAfterCancel = true;
        }
    }
    private async Task UninstallEditorAsync(InstalledEditor editor, CancellationToken token)
    {
        bool confirmed = await ConfirmAsync(
            Loc.Get("launcher.versions.uninstall_title", new
            {
                version = editor.Tag
            }),
            "launcher.versions.uninstall_description",
            "launcher.versions.uninstall", token);
        if (!confirmed)
        {
            return;
        }

        await Task.Run(() => _installer.Uninstall(editor), token);
        Notify("launcher.versions.uninstalled", Loc.Get("launcher.versions.uninstalled_description", new
        {
            version = editor.Tag
        }));
    }

    private void SetDefaultEditor(InstalledEditor editor, bool enabled)
    {
        store.Settings.DefaultEditorKey = enabled ? editor.Key : null;
        store.Save();
        Notify("launcher.versions.default_updated", enabled ? editor.Tag : "launcher.versions.automatic_selection");
    }

    private async Task RepairEditorAsync(InstalledEditor editor, CancellationToken token)
    {
        IReadOnlyList<EditorRelease> releases = await new GitHubReleasesService(_http, store, editor.Repository).GetAsync(token);
        EditorRelease release = releases.FirstOrDefault(release => release.Id == editor.ReleaseId && release.Tag == editor.Tag)
                                ?? throw new InvalidOperationException(Loc.Get("launcher.errors.release_unavailable"));
        await InstallAsync(release, token, true, editor.Repository);
    }

    private Task OpenInstallFolderAsync(CancellationToken token)
    {
        Directory.CreateDirectory(store.VersionsPath);
        Open(store.VersionsPath);
        return Task.CompletedTask;
    }
}
