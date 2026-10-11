using Prowl.OrigamiUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;
namespace Prowl.Launcher;

/// <summary>Confirms uninstalling the installed launcher, optionally with its settings and caches, then closes it.</summary>
sealed class UninstallDialog(LauncherStore store, Controls ui)
{
    private bool _removeData;

    /// <summary>True when this launcher was installed, so there is something to uninstall.</summary>
    public bool Available => store.Settings.InstalledLauncherExecutable != null;

    /// <summary>Set to show the prompt on the next frame, e.g. when started from Apps &amp; Features.</summary>
    public bool Pending { get; set; }

    public void ShowIfPending()
    {
        if (!Pending)
        {
            return;
        }
        Pending = false;
        if (Available)
        {
            Show();
        }
    }

    public void Show() => Origami.Modal(Loc.Get("launcher.uninstall.title"))
        .Width(Math.Min(520, ui.ScreenSize.X - 48))
        .CenteredContent(p =>
        {
            using (p.Column("uninstall-options").Gap(18).Height(UnitValue.Auto).Enter())
            {
                p.Box("uninstall-description")
                    .Height(UnitValue.Auto)
                    .Text(Loc.Get("launcher.uninstall.description"), ui.Font)
                    .FontSize(18).TextColor(ui.Ink).Wrap(TextWrapMode.Wrap).IsNotInteractable();
                Origami.Checkbox(p, "uninstall-remove-data", _removeData, value => _removeData = value)
                    .Label(Loc.Get("launcher.uninstall.remove_data"))
                    .Stretch().Show();
            }
        })
        .Button(Loc.Get("launcher.common.cancel"), Modal.Pop)
        .Button(Loc.Get("launcher.uninstall.confirm"), () =>
        {
            Modal.Pop();
            ui.Operations.Start(UninstallAsync, "launcher.uninstall.uninstalling");
        }, OrigamiVariant.Primary)
        .Show();

    private async Task UninstallAsync(CancellationToken token)
    {
        ui.Operations.ImmediateProgress = true;
        await new LauncherInstallationService(store).UninstallAsync(_removeData, token);
        ui.Operations.CloseWhenDone = true;
    }
}
