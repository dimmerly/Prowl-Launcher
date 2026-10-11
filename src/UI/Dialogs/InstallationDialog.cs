using Prowl.OrigamiUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;
namespace Prowl.Launcher;

/// <summary>
///     The first-run prompt to install the launcher or keep it portable, also shown from Settings. A <paramref name="preview" /> shows
///     the prompt without acting on either choice.
/// </summary>
sealed class InstallationDialog(LauncherStore store, Controls ui, bool preview)
{
    private bool _createDesktopShortcut = true;

    /// <summary>Set to show the prompt on the next frame.</summary>
    public bool Pending { get; set; }

    /// <summary>Shows the prompt if it is <see cref="Pending" />.</summary>
    public void ShowIfPending()
    {
        if (!Pending)
        {
            return;
        }
        Pending = false;
        Show();
    }

    /// <summary>True when this launcher isn't installed and can install itself.</summary>
    public bool Available => LauncherInstallationService.CanInstall(store);

    public void Show() => Origami.Modal(Loc.Get("launcher.installation.title"))
        .Width(Math.Min(520, ui.ScreenSize.X - 48))
        .CenteredContent(p =>
        {
            using (p.Column("installation-options").Gap(18).Height(UnitValue.Auto).Enter())
            {
                p.Box("installation-description")
                    .Height(UnitValue.Auto)
                    .Text(Loc.Get("launcher.installation.description"), ui.Font)
                    .FontSize(18).TextColor(ui.Ink).Wrap(TextWrapMode.Wrap).IsNotInteractable();
                Origami.Checkbox(p, "desktop-shortcut", _createDesktopShortcut, value => _createDesktopShortcut = value)
                    .Label(Loc.Get("launcher.installation.desktop_shortcut"))
                    .Stretch().Show();
            }
        })
        .Button(Loc.Get("launcher.installation.keep_portable"), () =>
        {
            Modal.Pop();
            if (!preview)
            {
                store.Settings.InstallationPromptHandled = true;
                store.Save();
            }
        })
        .Button(Loc.Get("launcher.installation.install"), () =>
        {
            Modal.Pop();
            if (!preview)
            {
                ui.Operations.Start(InstallAsync, "launcher.installation.installing");
            }
        }, OrigamiVariant.Primary)
        .Show();

    private async Task InstallAsync(CancellationToken token) =>
        ui.Operations.RestartWith = await new LauncherInstallationService(store).InstallAsync(_createDesktopShortcut, token);
}
