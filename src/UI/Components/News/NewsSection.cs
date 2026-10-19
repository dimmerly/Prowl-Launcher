using System.Diagnostics;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI.Markdown;
using Prowl.Rosetta;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.Launcher;

/// <summary>The news strip below the project list: paged preview cards that open full posts.</summary>
internal sealed class NewsSection(
    LauncherStore store,
    Controls ui,
    HttpClient http,
    Action<Exception> logError,
    BackgroundWork background,
    bool offline,
    bool capturing
) : IDisposable
{
    private const double NewsSlideDuration = 0.24;
    private const double LoadingOverlayDelay = 0.5;
    private readonly NewsImages _newsImages = new();
    private readonly NewsImages _newsThumbnails = new();
    private NewsService? _news;
    private bool _newsFailed;
    private bool _newsLoading;
    private long _newsLoadingStarted;
    private DateTime? _newsLocalStamp;
    private MarkdownBuilder? _newsMarkdown;
    private DateTime _newsNextCheck;
    private NewsPost? _newsOpening;
    private long _newsOpeningStarted;
    private int _newsPage;
    private IReadOnlyList<NewsPost> _newsPosts = [];
    private int _newsPreviousPage;
    private string? _newsRepository;
    private bool _newsRequested;
    private NewsPost? _newsSelected;
    private long _newsSlideStarted;

    private bool NewsSliding => _newsSlideStarted != 0
                                && Stopwatch.GetElapsedTime(_newsSlideStarted).TotalSeconds < NewsSlideDuration;

    /// <summary>Loading overlays wait briefly so instant loads never flash them.</summary>
    private bool ShowNewsLoading => _newsLoading && OverlayDue(_newsLoadingStarted);

    public void Dispose()
    {
        _newsImages.Dispose();
        _newsThumbnails.Dispose();
    }

    private static bool OverlayDue(long started) =>
        Stopwatch.GetElapsedTime(started).TotalSeconds >= LoadingOverlayDelay;

    private void ChangeNewsPage(int page)
    {
        if (NewsSliding || page == _newsPage)
        {
            return;
        }

        _newsPreviousPage = _newsPage;
        _newsPage = page;
        _newsSlideStarted = Stopwatch.GetTimestamp();
    }

    public void Draw(Paper p)
    {
        p.Box("news-divider").Height(1).BackgroundColor(ui.Appearance.Theme.BorderSoft).IsNotInteractable();
        using (p.Column("projects-news").Height(UnitValue.Auto).Gap(8).Enter())
        {
            DrawNews(p);
        }
    }

    private void DrawNews(Paper p)
    {
        if (_newsRepository != store.Settings.LauncherRepository)
        {
            _newsRepository = store.Settings.LauncherRepository;
            _newsImages.Dispose();
            _newsThumbnails.Dispose();
            _news = new NewsService(http, store, _newsRepository, NewsService.DevelopmentDirectory());
            _newsPosts = _news.ReadCache();
            _newsPage = 0;
            _newsSlideStarted = 0;
            _newsSelected = null;
            _newsMarkdown = null;
            _newsFailed = false;
            _newsRequested = false;
            _newsLocalStamp = _news.LocalIndexStamp;
            _newsNextCheck = default;
        }

        if (!_newsLoading && _news?.IsLocal == true && DateTime.UtcNow >= _newsNextCheck)
        {
            _newsNextCheck = DateTime.UtcNow.AddSeconds(1);
            if (_newsLocalStamp != _news.LocalIndexStamp)
            {
                _newsLocalStamp = _news.LocalIndexStamp;
                _newsRequested = false;
            }
        }

        if (_news?.IsLocal == false && DateTime.UtcNow >= _newsNextCheck)
        {
            _newsRequested = false;
        }

        if (!_newsRequested && !_newsLoading && (_news?.IsLocal == true || (!offline && !capturing)))
        {
            _ = RefreshNewsAsync();
        }

        DrawNewsPreviewCards(p);
        if (_newsPosts.Count == 0 && !_newsLoading && !_newsFailed)
        {
            ui.Label(p, "news-empty", "launcher.news.empty", 18, ui.Muted, 36);
        }

        if (_newsFailed)
        {
            ui.Label(p, "news-error", "launcher.news.error", 18, ui.Muted, 48);
        }
    }

    private void DrawNewsPreviewCards(Paper p)
    {
        int lastPage = Math.Max(0, (_newsPosts.Count - 1) / 3);
        _newsPage = Math.Clamp(_newsPage, 0, lastPage);
        if (_newsPosts.Count == 0 && !ShowNewsLoading)
        {
            return;
        }

        float available = Math.Max(1, Controls.ContentWidth(p));
        bool hasMore = lastPage > 0;
        float sideSpace = hasMore ? 28 : 0;
        using (p.Box("news-previews").Height(88).Enter())
        {
            float contentWidth = Math.Max(1, available - 2 * sideSpace);
            bool sliding = NewsSliding;
            float progress = sliding
                ? (float)(Stopwatch.GetElapsedTime(_newsSlideStarted).TotalSeconds / NewsSlideDuration)
                : 1;
            float eased = 1 - MathF.Pow(1 - progress, 3);
            int direction = Math.Sign(_newsPage - _newsPreviousPage);
            using (p.Box("news-preview-viewport").PositionType(PositionType.SelfDirected)
                       .Left(sideSpace).Top(0).Size(contentWidth, 88).Clip().Enter())
            {
                if (sliding)
                {
                    DrawNewsPreviewPage(p, _newsPreviousPage, contentWidth,
                        -direction * eased * (contentWidth + 12), true);
                }

                DrawNewsPreviewPage(p, _newsPage, contentWidth,
                    direction * (1 - eased) * (contentWidth + 12), sliding);
            }

            if (_newsPage > 0)
            {
                using (p.Box("news-newer-slot").PositionType(PositionType.SelfDirected).AnchorLeft(0).AnchorTop(0)
                           .Size(20, 88).Enter())
                {
                    Origami.Button(p, "news-newer", Loc.Get("launcher.news.newer"))
                        .IconOnly().LeadingIcon(OrigamiIconSet.ChevronLeft).Ghost().Width(20).Height(88)
                        .Tooltip(Loc.Get("launcher.news.newer")).Disabled(sliding)
                        .OnClick(() => ChangeNewsPage(Math.Max(0, _newsPage - 1))).Show();
                }
            }

            if (_newsPage < lastPage)
            {
                using (p.Box("news-older-slot").PositionType(PositionType.SelfDirected).AnchorRight(0).AnchorTop(0)
                           .Size(20, 88).Enter())
                {
                    Origami.Button(p, "news-older", Loc.Get("launcher.news.older"))
                        .IconOnly().LeadingIcon(OrigamiIconSet.ChevronRight).Ghost().Width(20).Height(88)
                        .Tooltip(Loc.Get("launcher.news.older")).Disabled(sliding)
                        .OnClick(() => ChangeNewsPage(Math.Min(lastPage, _newsPage + 1))).Show();
                }
            }

            if (ShowNewsLoading)
            {
                using (p.Column("news-loading").PositionType(PositionType.SelfDirected)
                           .Left(0).Top(0).Size(available, 88)
                           .Rounded(ui.Appearance.Theme.Metrics.ContainerRounding)
                           .BackgroundColor(OrigamiTheme.WithAlpha(ui.Appearance.Card, 200))
                           .AlignItems(LayoutAlignment.Center).JustifyContent(LayoutJustification.Center).Gap(8)
                           .IsNotInteractable().Enter())
                {
                    Origami.Spinner(p, "news-loading-spinner").Tint(ui.Muted).Show();
                    p.Box("news-loading-text").Width(UnitValue.Auto).Height(UnitValue.Auto).IsNotInteractable()
                        .Text(Loc.Get("launcher.news.loading"), ui.Font).FontSize(16).TextColor(ui.Muted);
                }
            }
        }
    }

    private void DrawNewsPreviewPage(Paper p, int page, float contentWidth, float offset, bool sliding)
    {
        NewsPost[] posts = _newsPosts.Skip(page * 3).Take(3).ToArray();
        if (posts.Length == 0)
        {
            return;
        }

        float width = Math.Max(1, (contentWidth - 24) / 3);
        using (p.Row("news-preview-page-" + page).PositionType(PositionType.SelfDirected)
                   .Left(0).Top(0).Size(contentWidth, 88).TranslateX(offset).Gap(12)
                   .JustifyContent(LayoutJustification.Start).Enter())
        {
            foreach (NewsPost post in posts)
            {
                ElementBuilder card = p.Row("news-preview-" + post.File).Width(width).Height(88)
                    .Padding(0).Gap(0).AlignItems(LayoutAlignment.Center)
                    .BackgroundColor(ui.Appearance.Card).BorderColor(ui.Appearance.Theme.BorderSoft).BorderWidth(1)
                    .Rounded(ui.Appearance.Theme.Metrics.ContainerRounding).Clip().Cursor(PaperCursor.Pointer)
                    .OnClick(click =>
                    {
                        if (!_newsLoading && _newsOpening == null && !NewsSliding)
                        {
                            _ = OpenNewsAsync(post);
                        }
                    });
                if (sliding)
                {
                    card.IsNotInteractable();
                }

                card.Hovered.BackgroundColor(ui.Appearance.Theme.Hover);
                using (card.Enter())
                {
                    if (width >= 260 && !string.IsNullOrWhiteSpace(post.Thumbnail) && _news != null)
                    {
                        TextureTK? thumbnail = _newsThumbnails.Get(_news, post, post.Thumbnail,
                            !offline && !capturing, background.Token, logError);
                        if (thumbnail != null)
                        {
                            DrawNewsThumbnail(p, thumbnail);
                        }
                    }

                    using (p.Column("info").Height(88).Padding(12).Gap(2).IsNotInteractable().Enter())
                    {
                        p.Box("title").Height(UnitValue.Auto).MaxHeight(40).Clip().IsNotInteractable()
                            .Text(post.Title, ui.Bold).FontSize(18).TextColor(ui.Ink).Wrap(TextWrapMode.Wrap);
                        p.Box("byline-space").Height(UnitValue.Stretch()).IsNotInteractable();
                        DrawNewsByline(p, post, 13);
                    }

                    if (_newsOpening == post && OverlayDue(_newsOpeningStarted))
                    {
                        using (p.Column("opening").PositionType(PositionType.SelfDirected)
                                   .Left(0).Top(0).Size(width, 88)
                                   .BackgroundColor(OrigamiTheme.WithAlpha(ui.Appearance.Card, 200))
                                   .AlignItems(LayoutAlignment.Center).JustifyContent(LayoutJustification.Center)
                                   .IsNotInteractable().Enter())
                        {
                            Origami.Spinner(p, "opening-spinner").Tint(ui.Muted).Show();
                        }
                    }
                }
            }
        }
    }

    private void DrawNewsThumbnail(Paper p, TextureTK texture)
    {
        float rounding = ui.Appearance.Theme.Metrics.ContainerRounding;
        using (p.Box("thumbnail").Size(96, 88).Clip().IsNotInteractable().Enter())
        {
            // Only the card's left corners are rounded; the text sits to the right.
            Controls.DrawCoverImage(p, texture, rounding, 0, 0, rounding);
        }
    }

    private void DrawNewsByline(Paper p, NewsPost post, float fontSize)
    {
        using (p.Row("byline").Height(UnitValue.Auto).Gap(12).IsNotInteractable().Enter())
        {
            string author = string.IsNullOrWhiteSpace(post.Author) ? "" : post.Author;
            p.Box("author").Height(UnitValue.Auto).Clip().IsNotInteractable()
                .Text(author, ui.Font).FontSize(fontSize).TextColor(ui.Muted);
            p.Box("date").Width(UnitValue.Auto).Height(UnitValue.Auto).IsNotInteractable()
                .Text(RelativeDate.Format(post.Date), ui.Font).FontSize(fontSize).TextColor(ui.Muted);
        }
    }

    private void NewsText(Paper p, string id, string text, bool heading = false) =>
        p.Box(id).Height(UnitValue.Auto).IsNotInteractable().Text(text, heading ? ui.Bold : ui.Font)
            .FontSize(heading ? 19 : 16).TextColor(heading ? ui.Ink : ui.Muted).Wrap(TextWrapMode.Wrap);

    private async Task RefreshNewsAsync()
    {
        if (_news == null || _newsLoading || (offline && !_news.IsLocal))
        {
            return;
        }

        NewsService service = _news;
        CancellationToken token = background.Token;
        _newsRequested = true;
        _newsNextCheck = DateTime.UtcNow.AddMinutes(_news?.IsLocal == true ? 0 : 15);
        _newsLoading = true;
        _newsLoadingStarted = Stopwatch.GetTimestamp();
        _newsFailed = false;
        try
        {
            IReadOnlyList<NewsPost> posts = await service.GetIndexAsync(token);
            if (_news == service)
            {
                _newsPosts = posts;
                _newsSlideStarted = 0;
                _newsThumbnails.Dispose();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Background work was restarted; refresh again with the new token.
            if (_news == service)
            {
                _newsRequested = false;
            }
        }
        catch (Exception error)
        {
            if (_news == service)
            {
                _newsFailed = true;
                _newsNextCheck = DateTime.UtcNow.AddMinutes(1);
            }

            logError(error);
        }
        finally
        {
            _newsLoading = false;
        }
    }

    private async Task OpenNewsAsync(NewsPost post)
    {
        if (_news == null || _newsLoading || _newsOpening != null)
        {
            return;
        }

        NewsService service = _news;
        CancellationToken token = background.Token;
        _newsSelected = post;
        _newsImages.Dispose();
        _newsMarkdown = null;
        _newsOpening = post;
        _newsOpeningStarted = Stopwatch.GetTimestamp();
        _newsFailed = false;
        try
        {
            string markdown = await service.GetPostAsync(post, !offline && !capturing, token);
            if (_news == service && _newsSelected == post)
            {
                string content = markdown.TrimStart();
                if (content.StartsWith("# ", StringComparison.Ordinal))
                {
                    int lineEnd = content.IndexOf('\n');
                    markdown = lineEnd < 0 ? "" : content[(lineEnd + 1)..];
                }

                _newsMarkdown = new MarkdownBuilder(ui.Font).Fonts(ui.Bold).FontSize(18).Source(markdown)
                    .Images(href => _newsImages.Get(service, post, href, !offline && !capturing,
                        background.Token, logError)!)
                    .OnLink(href =>
                    {
                        if (Uri.TryCreate(service.Page(post.File), href, out Uri? uri) &&
                            uri.Scheme is "https" or "http")
                        {
                            ProcessHelper.OpenInShell(uri.AbsoluteUri);
                        }
                    });
                ShowNewsArticle(post, _newsMarkdown);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (_news == service && _newsSelected == post)
            {
                _newsFailed = true;
            }

            logError(error);
        }
        finally
        {
            _newsOpening = null;
        }
    }

    private void ShowNewsArticle(NewsPost post, MarkdownBuilder markdown)
    {
        Float2 screen = ui.ScreenSize;
        float width = Math.Min(920, screen.X - 48);
        float height = Math.Max(100, Math.Min(600, screen.Y * 0.8f - 150));
        Origami.Modal(post.Title).Width(width)
            .CenteredContent(p => Origami.ScrollView(p, "news-article-scroll", width - 24, height)
                .OverlayScrollbars(false).Padding(0, 20, 0, 0)
                .ColSpacing(8).SmoothScroll().WheelStep(72).Body(() =>
                {
                    ui.StyleMarkdown(markdown).Build(p, "news-article-" + post.File);
                }))
            .Button(Loc.Get("launcher.common.close"), Modal.Pop, OrigamiVariant.Primary)
            .Show();
    }
}
