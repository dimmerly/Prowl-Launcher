using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;
namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private readonly SampleThumbnail _sampleThumbnails = new();
    private string? _launchingSampleId;
    private bool _sampleUpdateAvailable;
    private bool _sampleUpdateChecking;
    private string? _sampleUpdatesCheckedRepository;
    private void DrawSamplesHeadingActions(Paper p)
    {
        if (_sampleUpdateAvailable && _samples.IsCached
                                   && _sampleUpdatesCheckedRepository == store.Settings.LauncherRepository)
        {
            Button(p, "update-samples", "launcher.samples.update", UpdateSamplesAsync, width: 220);
        }
    }

    private void DrawSamples(Paper p)
    {
        if (!offline && screenshot == null && !Busy && _samples.IsCached && !_sampleUpdateChecking
            && _sampleUpdatesCheckedRepository != store.Settings.LauncherRepository)
        {
            _ = CheckSamplesInBackgroundAsync();
        }

        IReadOnlyList<Sample> samples = _samples.Catalog;
        if (samples.Count == 0)
        {
            return;
        }

        // Leave eight pixels on each edge for the hover scale to expand inside the scroll clip.
        float available = p.ScreenRect.Size.X - Constants.Layout.SidebarWidth - 72;
        int columns = Math.Clamp((int)((available + Constants.Layout.SampleGap) / 260), 1, samples.Count);
        float cardWidth = Math.Clamp((available - Constants.Layout.SampleGap * (columns - 1)) / columns, 1, 280);
        for (int first = 0; first < samples.Count; first += columns)
        {
            using (p.Row("sample-row-" + first).Height(cardWidth).Gap(Constants.Layout.SampleGap).Enter())
            {
                foreach (Sample sample in samples.Skip(first).Take(columns))
                {
                    DrawSampleCard(p, sample, cardWidth);
                }
            }
        }
    }

    private void DrawSampleCard(Paper p, Sample sample, float width)
    {
        string id = "sample-" + sample.Id;
        bool runnable = _samples.IsAvailable(sample);
        float rounding = _appearance.Theme.Metrics.ContainerRounding;
        ElementBuilder card = p.Column(id).Width(width).Height(width)
            .Gap(0).Rounded(rounding).Clip()
            .JustifyContent(LayoutJustification.End)
            .BackgroundColor(_appearance.Card)
            .BorderColor(_appearance.Theme.BorderSoft).BorderWidth(1)
            .Tooltip(runnable ? sample.Name : Loc.Get("launcher.samples.unavailable"))
            .OnClick(_ =>
            {
                if (!Busy && runnable)
                {
                    Start(token => RunSampleAsync(sample, token), "launcher.samples.run");
                }
            });
        if (runnable && !Busy)
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
                    DrawCoverImage(p, texture, rounding, rounding, rounding, rounding);
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
                    .Text(sample.Name, _bold).FontSize(21).TextColor(Color.White)
                    .Wrap(TextWrapMode.Wrap);
                string description = runnable ? sample.Description : Loc.Get("launcher.samples.unavailable");
                if (!string.IsNullOrWhiteSpace(description))
                {
                    p.Box(id + "description").Height(UnitValue.Auto).IsNotInteractable()
                        .Text(description, _font)
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
            _immediateProgress = !_samples.IsCached;
            if (_immediateProgress)
            {
                _operationTitle = "launcher.samples.downloading";
            }
            await _samples.EnsureDownloadedAsync(Transfer(), token, !offline);
            _immediateProgress = false;
            _operationTitle = "launcher.samples.run";
            await _samples.RunAsync(sample, Transfer(), token, !offline);
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
        _sampleUpdateChecking = true;
        try
        {
            bool available = await _samples.HasUpdateAsync(_backgroundCancellation.Token);
            if (!_backgroundCancellation.IsCancellationRequested && store.Settings.LauncherRepository == repository)
            {
                _sampleUpdateAvailable = available;
            }
        }
        catch (OperationCanceledException) when (_backgroundCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            LogError(error);
        }
        finally
        {
            _sampleUpdateChecking = false;
            _sampleUpdatesCheckedRepository = repository;
        }
    }

    private async Task UpdateSamplesAsync(CancellationToken token)
    {
        _immediateProgress = true;
        _operationTitle = "launcher.samples.downloading";
        await _samples.EnsureDownloadedAsync(Transfer(), token, checkForUpdates: true);
        _sampleUpdateAvailable = false;
        Notify("launcher.samples.updated", "launcher.samples.subtitle");
    }
}
