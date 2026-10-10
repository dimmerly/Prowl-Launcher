using System.Diagnostics;
using System.Drawing;

using OpenTK.Mathematics;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI.Markdown;
using Prowl.Rosetta;
using Prowl.Scribe;
using Prowl.Vector.Spatial;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private NewsService? _news;
    private string? _newsRepository;
    private IReadOnlyList<NewsPost> _newsPosts = [];
    private int _newsPage;
    private int _newsPreviousPage;
    private long _newsSlideStarted;
    private const double NewsSlideDuration = 0.24;

    private bool NewsSliding => _newsSlideStarted != 0
        && Stopwatch.GetElapsedTime(_newsSlideStarted).TotalSeconds < NewsSlideDuration;

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
    private bool _newsLoading;
    private bool _newsRequested;
    private bool _newsFailed;
    private DateTime? _newsLocalStamp;
    private DateTime _newsNextCheck;
    private NewsPost? _newsSelected;
    private MarkdownBuilder? _newsMarkdown;
    private readonly NewsImages _newsImages = new();
    private readonly NewsImages _newsThumbnails = new();

    private void DrawNewsSection(Paper p)
    {
        p.Box("news-divider").Height(1).BackgroundColor(_appearance.Theme.BorderSoft).IsNotInteractable();
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
            _news = new NewsService(_http, store, _newsRepository, NewsService.DevelopmentDirectory());
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

        if (!_newsRequested && !_newsLoading && (_news?.IsLocal == true || !offline && screenshot == null))
        {
            _ = RefreshNewsAsync();
        }

        DrawNewsPreviewCards(p);
        if (_newsPosts.Count == 0 && !_newsLoading && !_newsFailed)
        {
            Label(p, "news-empty", "launcher.news.empty", 18, Muted, 36);
        }

        if (_newsLoading)
        {
            Label(p, "news-loading", "launcher.news.loading", 18, Muted, 36);
        }

        if (_newsFailed)
        {
            Label(p, "news-error", "launcher.news.error", 18, Muted, 48);
        }
    }

    private void DrawNewsPreviewCards(Paper p)
    {
        int lastPage = Math.Max(0, (_newsPosts.Count - 1) / 3);
        _newsPage = Math.Clamp(_newsPage, 0, lastPage);
        if (_newsPosts.Count == 0)
        {
            return;
        }

        float available = Math.Max(1, (float)p.ScreenRect.Size.X - Constants.Layout.SidebarWidth - 56);
        bool hasMore = lastPage > 0;
        float sideSpace = hasMore ? 28 : 0;
        using (p.Box("news-previews").Height(88).Enter())
        {
            float contentWidth = Math.Max(1, available - 2 * sideSpace);
            bool sliding = NewsSliding;
            float progress = sliding
                ? (float)(Stopwatch.GetElapsedTime(_newsSlideStarted).TotalSeconds / NewsSlideDuration) : 1;
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
                    .BackgroundColor(_appearance.Card).BorderColor(_appearance.Theme.BorderSoft).BorderWidth(1)
                    .Rounded(_appearance.Theme.Metrics.ContainerRounding).Clip().Cursor(PaperCursor.Pointer)
                    .OnClick(click => { if (!_newsLoading && !NewsSliding) { _ = OpenNewsAsync(post); } });
                if (sliding)
                {
                    card.IsNotInteractable();
                }

                card.Hovered.BackgroundColor(_appearance.Theme.Hover);
                using (card.Enter())
                {
                    if (width >= 260 && !string.IsNullOrWhiteSpace(post.Thumbnail) && _news != null)
                    {
                        TextureTK? thumbnail = _newsThumbnails.Get(_news, post, post.Thumbnail,
                            !offline && screenshot == null, _backgroundCancellation.Token, LogError);
                        if (thumbnail != null)
                        {
                            DrawNewsThumbnail(p, thumbnail);
                        }
                    }
                    using (p.Column("info").Height(88).Padding(12).Gap(2).IsNotInteractable().Enter())
                    {
                        p.Box("title").Height(UnitValue.Auto).MaxHeight(40).Clip().IsNotInteractable()
                            .Text(post.Title, _bold).FontSize(18).TextColor(Ink).Wrap(TextWrapMode.Wrap);
                        p.Box("byline-space").Height(UnitValue.Stretch()).IsNotInteractable();
                        DrawNewsByline(p, post, 13);
                    }
                }
            }
        }
    }

    private void DrawNewsThumbnail(Paper p, TextureTK texture)
    {
        float rounding = _appearance.Theme.Metrics.ContainerRounding;
        using (p.Box("thumbnail").Size(96, 88).Clip().IsNotInteractable().Enter())
        {
            p.Draw((canvas, rect) =>
            {
                float width = (float)rect.Size.X, height = (float)rect.Size.Y;
                float scale = Math.Max(width / texture.Width, height / texture.Height);
                float imageWidth = texture.Width * scale, imageHeight = texture.Height * scale;
                float x = (float)rect.Min.X, y = (float)rect.Min.Y;
                canvas.SaveState();
                canvas.SetBrushTexture(texture);
                canvas.SetBrushTextureTransform(
                    Transform2D.CreateTranslation(x + (width - imageWidth) / 2, y + (height - imageHeight) / 2)
                    * Transform2D.CreateScale(imageWidth, imageHeight));
                canvas.RoundedRectFilled(x, y, width, height, rounding, 0, 0, rounding, Color.White);
                canvas.RestoreState();
            });
        }
    }

    private void DrawNewsByline(Paper p, NewsPost post, float fontSize)
    {
        using (p.Row("byline").Height(UnitValue.Auto).Gap(12).IsNotInteractable().Enter())
        {
            string author = string.IsNullOrWhiteSpace(post.Author) ? "" : post.Author;
            p.Box("author").Height(UnitValue.Auto).Clip().IsNotInteractable()
                .Text(author, _font).FontSize(fontSize).TextColor(Muted);
            p.Box("date").Width(UnitValue.Auto).Height(UnitValue.Auto).IsNotInteractable()
                .Text(RelativeDate.Format(post.Date), _font).FontSize(fontSize).TextColor(Muted);
        }
    }

    private void NewsText(Paper p, string id, string text, bool heading = false) =>
        p.Box(id).Height(UnitValue.Auto).IsNotInteractable().Text(text, heading ? _bold : _font)
            .FontSize(heading ? 19 : 16).TextColor(heading ? Ink : Muted).Wrap(TextWrapMode.Wrap);

    private async Task RefreshNewsAsync()
    {
        if (_news == null || _newsLoading || offline && !_news.IsLocal)
        {
            return;
        }

        NewsService service = _news;
        _newsRequested = true;
        _newsNextCheck = DateTime.UtcNow.AddMinutes(_news?.IsLocal == true ? 0 : 15);
        _newsLoading = true;
        _newsFailed = false;
        try
        {
            IReadOnlyList<NewsPost> posts = await service.GetIndexAsync(_backgroundCancellation.Token);
            if (_news == service)
            {
                _newsPosts = posts;
                _newsSlideStarted = 0;
                _newsThumbnails.Dispose();
            }
        }
        catch (OperationCanceledException) when (_backgroundCancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_news == service)
            {
                _newsFailed = true;
                _newsNextCheck = DateTime.UtcNow.AddMinutes(1);
            }
            LogError(error);
        }
        finally { _newsLoading = false; }
    }

    private async Task OpenNewsAsync(NewsPost post)
    {
        if (_news == null || _newsLoading)
        {
            return;
        }

        NewsService service = _news;
        _newsSelected = post;
        _newsImages.Dispose();
        _newsMarkdown = null;
        _newsLoading = true;
        _newsFailed = false;
        try
        {
            string markdown = await service.GetPostAsync(post, !offline && screenshot == null, _backgroundCancellation.Token);
            if (_news == service && _newsSelected == post)
            {
                string content = markdown.TrimStart();
                if (content.StartsWith("# ", StringComparison.Ordinal))
                {
                    int lineEnd = content.IndexOf('\n');
                    markdown = lineEnd < 0 ? "" : content[(lineEnd + 1)..];
                }
                _newsMarkdown = new MarkdownBuilder(_font).Fonts(bold: _bold).FontSize(18).Source(markdown)
                    .Images(href => _newsImages.Get(service, post, href, !offline && screenshot == null,
                        _backgroundCancellation.Token, LogError)!)
                    .OnLink(href =>
                    {
                        if (Uri.TryCreate(service.Page(post.File), href, out Uri? uri) && uri.Scheme is "https" or "http")
                        {
                            Open(uri.AbsoluteUri);
                        }
                    });
                ShowNewsArticle(post, _newsMarkdown);
            }
        }
        catch (OperationCanceledException) when (_backgroundCancellation.IsCancellationRequested) { }
        catch (Exception error) { if (_news == service && _newsSelected == post) { _newsFailed = true; } LogError(error); }
        finally { _newsLoading = false; }
    }

    private void ShowNewsArticle(NewsPost post, MarkdownBuilder markdown)
    {
        Vector2i framebuffer = _window.FramebufferSize;
        float scale = Math.Max(0.01f, DisplayScale / UiScale) * UiScale;
        float width = Math.Min(920, framebuffer.X / scale - 48);
        float height = Math.Max(100, Math.Min(600, framebuffer.Y / scale * 0.8f - 150));
        Origami.Modal(post.Title).Width(width)
            .CenteredContent(p => Origami.ScrollView(p, "news-article-scroll", width - 24, height)
                .OverlayScrollbars(false).Padding(0, 20, 0, 0)
                .ColSpacing(8).SmoothScroll(true).WheelStep(72).Body(() =>
                {
                    markdown.Colors(Ink, Muted, _appearance.Accent, _appearance.Panel, _appearance.Theme.BorderSoft)
                        .Build(p, "news-article-" + post.File);
                }))
            .Button(Loc.Get("launcher.common.close"), Modal.Pop, OrigamiVariant.Primary)
            .Show();
    }
}
