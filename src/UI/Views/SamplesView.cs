using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;

namespace Prowl.Launcher;

/// <summary>The sample catalog: run a sample, and update the sample bundle when a newer one exists.</summary>
internal sealed class SamplesView(
    LauncherStore store,
    Controls ui,
    NotificationCenter notifications,
    SampleService samples,
    Action<Exception> logError,
    BackgroundWork background,
    bool offline,
    bool capturing
) : IDisposable
{
    private readonly SampleThumbnail _sampleThumbnails = new();
    private string? _launchingSampleId;
    private bool _sampleUpdateAvailable;
    private bool _sampleUpdateChecking;
    private string? _sampleUpdatesCheckedRepository;

    /// <summary>True while a sample starts; its card shows a spinner instead of the progress panel.</summary>
    public bool Launching => _launchingSampleId != null;

    public void Dispose() => _sampleThumbnails.Dispose();

    public void DrawHeadingActions(Paper p)
    {
        if (_sampleUpdateAvailable && samples.IsCached
                                   && _sampleUpdatesCheckedRepository == store.Settings.LauncherRepository)
        {
            ui.Button(p, "update-samples", "launcher.samples.update", UpdateSamplesAsync, width: 220);
        }
    }

    public void Draw(Paper p)
    {
        if (!offline && !capturing && !ui.Busy && samples.IsCached && !_sampleUpdateChecking
            && _sampleUpdatesCheckedRepository != store.Settings.LauncherRepository)
        {
            _ = CheckSamplesInBackgroundAsync();
        }

        IReadOnlyList<Sample> catalog = samples.Catalog;
        if (catalog.Count == 0)
        {
            return;
        }

        // Leave eight pixels on each edge for the hover scale to expand inside the scroll clip.
        float available = p.ScreenRect.Size.X - Constants.Layout.SidebarWidth - 72;
        int columns = Math.Clamp((int)((available + Constants.Layout.SampleGap) / 260), 1, catalog.Count);
        float cardWidth = Math.Clamp((available - Constants.Layout.SampleGap * (columns - 1)) / columns, 1, 280);
        for (int first = 0; first < catalog.Count; first += columns)
        {
            using (p.Row("sample-row-" + first).Height(cardWidth).Gap(Constants.Layout.SampleGap).Enter())
            {
                foreach (Sample sample in catalog.Skip(first).Take(columns))
                {
                    DrawSampleCard(p, sample, cardWidth);
                }
            }
        }
    }

    private void DrawSampleCard(Paper p, Sample sample, float width)
    {
        string id = "sample-" + sample.Id;
        bool runnable = samples.IsAvailable(sample);
        float rounding = ui.Appearance.Theme.Metrics.ContainerRounding;
        ElementBuilder card = p.Column(id).Width(width).Height(width)
            .Gap(0).Rounded(rounding).Clip()
            .JustifyContent(LayoutJustification.End)
            .BackgroundColor(ui.Appearance.Card)
            .BorderColor(ui.Appearance.Theme.BorderSoft).BorderWidth(1)
            .Tooltip(runnable ? sample.Name : Loc.Get("launcher.samples.unavailable"))
            .OnClick(_ =>
            {
                if (!ui.Busy && runnable)
                {
                    ui.Operations.Start(token => RunSampleAsync(sample, token), "launcher.samples.run");
                }
            });
        if (runnable && !ui.Busy)
        {
            card.Cursor(PaperCursor.Pointer);
            card.Transition(GuiProp.ScaleX, 0.16f).Transition(GuiProp.ScaleY, 0.16f);
            card.Hovered.Scale(1.025f, 1.025f);
        }

        using (card.Enter())
        {
            using (p.Box(id + "image").PositionType(PositionType.SelfDirected)
                       .Left(0).Top(0).Size(width, width).Clip().IsNotInteractable().Enter())
            {
                TextureTK? texture = _sampleThumbnails.Get(sample);
                if (texture is not null)
                {
                    Controls.DrawCoverImage(p, texture, rounding, rounding, rounding, rounding);
                }
            }

            float captionHeight = Math.Max(80, width * 0.25f);
            p.Box(id + "shade").PositionType(PositionType.SelfDirected)
                .Left(0).Top(width - captionHeight - 12).Size(width, captionHeight + 12)
                .Rounded(0, 0, rounding, rounding)
                .BackgroundLinearGradient(0, 0, 0, 0.55f,
                    Color.FromArgb(0, 5, 8, 13), Color.FromArgb(245, 5, 8, 13))
                .IsNotInteractable();
            using (p.Column(id + "footer").Height(UnitValue.Auto).Padding(10).Gap(4)
                       .IsNotInteractable().Enter())
            {
                p.Box(id + "title").Height(UnitValue.Auto).IsNotInteractable()
                    .Text(sample.Name, ui.Bold).FontSize(21).TextColor(Color.White)
                    .Wrap(TextWrapMode.Wrap);
                string description = runnable ? sample.Description : Loc.Get("launcher.samples.unavailable");
                if (!string.IsNullOrWhiteSpace(description))
                {
                    p.Box(id + "description").Height(UnitValue.Auto).IsNotInteractable()
                        .Text(description, ui.Font)
                        .FontSize(17).TextColor(Color.FromArgb(235, 239, 246)).Wrap(TextWrapMode.Wrap);
                }
            }

            if (_launchingSampleId == sample.Id)
            {
                using (p.Column(id + "loading").PositionType(PositionType.SelfDirected)
                           .Left(0).Top(0).Size(width, width)
                           .Rounded(rounding)
                           .BackgroundColor(Color.FromArgb(160, 5, 8, 13))
                           .AlignItems(LayoutAlignment.Center).JustifyContent(LayoutJustification.Center)
                           .IsNotInteractable().Enter())
                {
                    Origami.Spinner(p, id + "spinner").XL().Tint(Color.White).Show();
                }
            }
        }
    }

    /// <summary>The first run downloads the bundle with visible progress; later runs only show the card spinner.</summary>
    private async Task RunSampleAsync(Sample sample, CancellationToken token)
    {
        _launchingSampleId = sample.Id;
        try
        {
            ui.Operations.ImmediateProgress = !samples.IsCached;
            if (ui.Operations.ImmediateProgress)
            {
                ui.Operations.Title = "launcher.samples.downloading";
            }

            await samples.EnsureDownloadedAsync(ui.Operations.Transfer(), token, !offline);
            ui.Operations.ImmediateProgress = false;
            ui.Operations.Title = "launcher.samples.run";
            await samples.RunAsync(sample, ui.Operations.Transfer(), token, !offline);
        }
        finally
        {
            _launchingSampleId = null;
        }
    }

    /// <summary>Checks once per launcher repository whether a newer bundle exists.</summary>
    private async Task CheckSamplesInBackgroundAsync()
    {
        string repository = store.Settings.LauncherRepository;
        CancellationToken token = background.Token;
        _sampleUpdateChecking = true;
        try
        {
            bool available = await samples.HasUpdateAsync(token);
            if (!token.IsCancellationRequested && store.Settings.LauncherRepository == repository)
            {
                _sampleUpdateAvailable = available;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            logError(error);
        }
        finally
        {
            _sampleUpdateChecking = false;
            // A cancelled check is retried with the new token on the next frame.
            if (!token.IsCancellationRequested)
            {
                _sampleUpdatesCheckedRepository = repository;
            }
        }
    }

    private async Task UpdateSamplesAsync(CancellationToken token)
    {
        ui.Operations.ImmediateProgress = true;
        ui.Operations.Title = "launcher.samples.downloading";
        await samples.EnsureDownloadedAsync(ui.Operations.Transfer(), token, checkForUpdates: true);
        _sampleUpdateAvailable = false;
        notifications.Notify("launcher.samples.updated", "launcher.samples.subtitle");
    }
}
