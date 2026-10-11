using Prowl.Launcher.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

using Color=System.Drawing.Color;

namespace Prowl.Launcher;

/// <summary>Appearance, preferences, launcher updates and storage, and the release repositories.</summary>
sealed class SettingsView(
    LauncherStore store,
    Controls ui,
    NotificationCenter notifications,
    EditorCoordinator editors,
    LauncherUpdateCoordinator updates,
    InstallationDialog installation,
    UninstallDialog uninstall,
    BackgroundWork background
)
{
    private string? _launcherRepositoryDraft;
    private string _launcherStorageSize = "—";
    private DateTimeOffset _nextStorageSizeRefresh;
    private string? _prowlRepositoryDraft;
    private Task? _storageSizeRefresh;
    private void DrawRepositorySettings(Paper p)
    {
        using (ui.Card(p, "repository-settings"))
        {
            ui.Label(p, "repositories-title", "launcher.settings.repositories", 19, ui.Ink, 28, true);
            ui.Label(
                p,
                "repositories-info",
                "launcher.settings.repository_description",
                13,
                ui.Muted,
                26
            );
            _prowlRepositoryDraft ??= store.Settings.ProwlRepository;
            ui.Label(p, "prowl-repository-label", "launcher.settings.prowl_releases", 14, ui.Ink, 26);
            Origami.TextField(p, "prowl-repository", _prowlRepositoryDraft, value => _prowlRepositoryDraft = value).Show();

            _launcherRepositoryDraft ??= store.Settings.LauncherRepository;
            ui.Label(p, "launcher-repository-label", "launcher.settings.launcher_releases", 14, ui.Ink, 26);
            Origami.TextField(p, "launcher-repository", _launcherRepositoryDraft, value => _launcherRepositoryDraft = value).Show();
            using (p.Row("repository-actions")
                .Height(UnitValue.Auto)
                .WrapContent()
                .Gap(8)
                .Enter())
            {
                ui.Button(p, "save-repositories", "launcher.settings.save_repositories", SaveRepositoriesAsync, true, 170);
                ui.Button(p, "reset-repositories", "launcher.settings.official_repository", ResetRepositoryDraftsAsync, width: 165);
            }
        }
    }

    public void Draw(Paper p)
    {
        RefreshStorageSizeIfDue();
        using (ui.Card(p, "appearance-settings"))
        {
            ui.Label(p, "appearance-title", "launcher.settings.appearance", 19, ui.Ink, 28, true);
            ui.Label(
                p,
                "appearance-info",
                "launcher.settings.appearance_description",
                13,
                ui.Muted,
                26
            );
            using (p.Row("interface-size-row")
                .Height(42)
                .Gap(12)
                .Enter())
            {
                ui.Label(p, "interface-size-label", "launcher.settings.interface_size", 14, ui.Ink, 44);
                Origami.Dropdown(
                        p,
                        "interface-size",
                        (int)Math.Round(WindowScale.ClampInterfaceScale(store.Settings.UiScale) * 100),
                        value =>
                        {
                            store.Settings.UiScale = value / 100f;
                            store.Save();
                        },
                        new[]
                        {
                            50,
                            65,
                            75,
                            90,
                            100,
                            115,
                            125,
                            150
                        }
                    )
                    .Display(value => value + "%")
                    .Width(260)
                    .Show();
            }

            using (p.Row("theme-preset-row")
                .Height(42)
                .Gap(12)
                .Enter())
            {
                ui.Label(p, "preset-label", "launcher.settings.theme", 14, ui.Ink, 34);
                string[] names = ThemePresets.All.Select(preset => preset.Name).ToArray();
                Origami.Dropdown(p, "theme-preset", ui.Appearance.Data.Name, name => ui.Appearance.UsePreset(name), names)
                    .Width(260)
                    .Show();
            }

            using (p.Row("theme-roundness-row")
                .Height(42)
                .Gap(12)
                .Enter())
            {
                ui.Label(p, "roundness-label", "launcher.settings.roundness", 14, ui.Ink, 34);
                Origami.Slider(p, "theme-roundness", ui.Appearance.Data.Roundness, value =>
                    {
                        ui.Appearance.Data.Roundness = MathF.Round(value, 2);
                        ui.Appearance.Save();
                    }, 0, 20)
                    .Format("F2")
                    .Width(260)
                    .Show();
            }

            ColorSetting(
                p,
                "accent-color",
                "launcher.settings.accent",
                ui.Appearance.Data.Purple.Primary,
                value => CustomizeRamp(ui.Appearance.Data.Purple, value)
            );
            ColorSetting(
                p,
                "logo-color",
                "launcher.settings.logo_color",
                store.Settings.LogoColor ?? ColorRamp.ColorToHex(ui.Ink),
                value =>
                {
                    store.Settings.LogoColor = value;
                    store.Save();
                }
            );
            ColorSetting(
                p,
                "panel-color",
                "launcher.settings.panels",
                ui.Appearance.Data.Neutral.Primary,
                value => CustomizeRamp(ui.Appearance.Data.Neutral, value)
            );
            ColorSetting(
                p,
                "text-color",
                "launcher.settings.text",
                ui.Appearance.Data.Ink.Primary,
                value => CustomizeRamp(ui.Appearance.Data.Ink, value)
            );
            ColorSetting(
                p,
                "background-color",
                "launcher.settings.background",
                ColorRamp.ColorToHex(ui.Appearance.Background),
                value =>
                {
                    ui.Appearance.Data.BackgroundStyle = EditorBackgroundStyle.Color;
                    ui.Appearance.Data.BackgroundColorA = value;
                    ui.Appearance.Save();
                }
            );
            using (p.Row("appearance-actions")
                .Height(UnitValue.Auto)
                .WrapContent()
                .Gap(8)
                .Enter())
            {
                Origami.Button(p, "use-editor-theme", Loc.Get("launcher.settings.use_editor_theme"))
                    .Width(190)
                    .Height(40)
                    .LeadingIcon(OrigamiIconSet.Pencil)
                    .OnClick(ui.Appearance.UseEditorTheme)
                    .Show();
                Origami.Button(p, "reset-theme", Loc.Get("launcher.settings.reset_to_default"))
                    .Width(190)
                    .Height(40)
                    .LeadingIcon(LauncherIcons.Refresh)
                    .OnClick(ResetAppearance)
                    .Show();
            }
        }

        using (ui.Card(p, "launch-settings"))
        {
            ui.Label(p, "launch-settings-title", "launcher.settings.preferences", 19, ui.Ink, 28, true);
            Origami.Switch(p, "close-on-editor-launch", store.Settings.CloseOnEditorLaunch, value =>
                {
                    store.Settings.CloseOnEditorLaunch = value;
                    store.Save();
                })
                .LabelLeft(Loc.Get("launcher.settings.close_on_launch"))
                .Stretch()
                .Show();
            Origami.Switch(p, "show-fps", store.Settings.ShowFps, value =>
                {
                    store.Settings.ShowFps = value;
                    store.Save();
                })
                .LabelLeft(Loc.Get("launcher.settings.show_fps"))
                .Stretch()
                .Show();
        }

        using (ui.Card(p, "launcher-settings"))
        {
            using (p.Row("launcher-storage-heading").Height(28).Enter())
            {
                ui.Label(p, "launcher-settings-title", "launcher.settings.updates_and_storage", 19, ui.Ink, 28, true);
                p.Box("launcher-file-size")
                    .Width(110)
                    .Height(28)
                    .IsNotInteractable()
                    .Text(_launcherStorageSize, ui.Font)
                    .FontSize(16)
                    .TextColor(ui.Muted)
                    .Alignment(TextAlignment.MiddleRight);
            }
            ui.Label(p, "launcher-version", Loc.Get("launcher.settings.version", new
            {
                version = VersionHelper.LauncherVersion, platform = Platform.Identifier
            }), 13, ui.Muted, 24);

            Origami.Toggle(p, "launcher-auto-update", store.Settings.LauncherAutoUpdate, updates.SetAutoUpdate)
                .LabelLeft(Loc.Get("launcher.settings.launcher_auto_update"))
                .Stretch().Disabled(ui.Busy).Show();

            Origami.Toggle(p, "launcher-prereleases", store.Settings.LauncherPrereleases, updates.SetPrereleases)
                .LabelLeft(Loc.Get("launcher.settings.launcher_prereleases"))
                .Stretch().Disabled(ui.Busy).Show();

            using (p.Row("launcher-settings-actions")
                .Height(UnitValue.Auto)
                .WrapContent()
                .Gap(8)
                .Enter())
            {
                ui.Button(p, "launcher-update", "launcher.updates.check", updates.UpdateAsync, width: 160);
                ui.Button(
                    p,
                    "data-folder",
                    "launcher.settings.open_data_folder",
                    _ =>
                    {
                        ProcessHelper.OpenInShell(store.Home);
                        return Task.CompletedTask;
                    },
                    width: 155
                );
                if (installation.Available)
                {
                    ui.Button(p, "install-launcher", "launcher.installation.button", _ =>
                    {
                        installation.Show();
                        return Task.CompletedTask;
                    }, width: 170);
                }
                if (uninstall.Available)
                {
                    ui.Button(p, "uninstall-launcher", "launcher.uninstall.button", _ =>
                    {
                        uninstall.Show();
                        return Task.CompletedTask;
                    }, width: 170);
                }
            }
        }

        DrawRepositorySettings(p);
    }

    private void CustomizeRamp(ColorRamp ramp, string value)
    {
        ramp.Primary = value;
        ramp.OverrideAll = false;
        ui.Appearance.Save();
    }

    private void ColorSetting(Paper p, string id, string label, string hex, Action<string> changed)
    {
        using (p.Row(id + "-row")
            .Height(42)
            .Gap(12)
            .Enter())
        {
            ui.Label(p, id + "-label", label, 14, ui.Ink, 34);
            Color color = ColorRamp.ParseHex(hex);
            Origami.ColorField(
                    p,
                    id,
                    new Vector.Color(color.R / 255f, color.G / 255f, color.B / 255f, 1),
                    value => changed(ColorRamp.ColorToHex(Color.FromArgb(
                        255,
                        (int)(Math.Clamp(value.R, 0, 1) * 255),
                        (int)(Math.Clamp(value.G, 0, 1) * 255),
                        (int)(Math.Clamp(value.B, 0, 1) * 255)
                    )))
                )
                .Width(260)
                .Height(40)
                .Show();
        }
    }

    /// <summary>Refreshes the launcher home size shown in settings, at most every few seconds.</summary>
    private void RefreshStorageSizeIfDue()
    {
        if (_storageSizeRefresh is { IsCompleted: false } || DateTimeOffset.UtcNow < _nextStorageSizeRefresh)
        {
            return;
        }
        _nextStorageSizeRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
        _storageSizeRefresh = RefreshStorageSizeAsync();
    }

    private async Task RefreshStorageSizeAsync()
    {
        CancellationToken token = background.Token;
        try
        {
            long bytes = await Task.Run(() => DirectorySize(store.Home, token), token);
            _launcherStorageSize = bytes >= 1073741824
                ? $"{bytes / 1073741824d:F1} GB"
                : $"{bytes / 1048576d:F1} MB";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _launcherStorageSize = "—";
        }
    }

    private static long DirectorySize(string root, CancellationToken token)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
        };
        long total = 0;
        foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                total += file.Length;
            }
            catch (IOException)
            {
                // An installation or update may remove files during the scan.
            }
        }
        return total;
    }

    /// <summary>Saves both repository drafts and reloads editor releases from the new source.</summary>
    private async Task SaveRepositoriesAsync(CancellationToken token)
    {
        string prowl = GitHubRepository.Normalize(_prowlRepositoryDraft ?? store.Settings.ProwlRepository);
        string launcher = GitHubRepository.Normalize(_launcherRepositoryDraft ?? store.Settings.LauncherRepository);
        // Results from the old repositories must not land after the switch.
        background.Restart();

        store.Settings.ProwlRepository = prowl;
        store.Settings.LauncherRepository = launcher;
        _prowlRepositoryDraft = prowl;
        _launcherRepositoryDraft = launcher;
        store.Save();
        editors.ClearReleases();

        notifications.Notify("launcher.settings.repository_saved", prowl);
        await editors.RefreshAsync(token, false);
    }

    private Task ResetRepositoryDraftsAsync(CancellationToken token)
    {
        _prowlRepositoryDraft = Constants.Defaults.ProwlRepository;
        _launcherRepositoryDraft = Constants.Defaults.LauncherRepository;
        return Task.CompletedTask;
    }

    private void ResetAppearance()
    {
        Settings defaults = new();
        store.Settings.UiScale = defaults.UiScale;
        store.Settings.LogoColor = defaults.LogoColor;
        store.Save();
        ui.Appearance.ResetToDefault();
    }
}
