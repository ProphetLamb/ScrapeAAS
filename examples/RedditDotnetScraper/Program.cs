using ScrapeAAS;
using RedditDotnetScraper;
using Dawn;
using AngleSharp.Dom;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddScrapeAAS()
    .AddHostedService<TopLevelRedditPostSpider>()
    .AddDataFlowHandler<RedditTopLevelPostBrief, RedditTopLevelPostCommentsSpider>();
var app = builder.Build();
app.Run();

sealed record RedditUserId(string Id);

sealed record RedditTopLevelPost(RedditTopLevelPostBrief Brief, RedditComment Comment);
sealed record RedditTopLevelPostBrief(Url PostUrl, string Title, long Upvotes, long Comments, Url CommentsUrl, DateTimeOffset PostedAt, RedditUserId PostedBy);
sealed record RedditComment(Url ParentCommentUrl, Url CommentUrl, string HtmlText, DateTimeOffset PostedAt, RedditUserId PostedBy);

sealed class TopLevelRedditPostSpider : BackgroundService
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
    private readonly ILogger<TopLevelRedditPostSpider> _logger;
    private readonly IDataflowPublisher<RedditTopLevelPostBrief> _publisher;

    public TopLevelRedditPostSpider(IAngleSharpBrowserPageLoader browserPageLoader, ILogger<TopLevelRedditPostSpider> logger, IDataflowPublisher<RedditTopLevelPostBrief> publisher)
    {
        _browserPageLoader = browserPageLoader;
        _logger = logger;
        _publisher = publisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Url root = new("https://old.reddit.com");
        Url source = new(root, "/r/dotnet/");
        var document = await _browserPageLoader.LoadAsync(source, stoppingToken);
        var queriedContent = document
            .QuerySelectorAll("div.thing")
            .Select(div => new
            {
                PostUrl = div.QuerySelector("a.title")?.GetAttribute("href"),
                Title = div.QuerySelector("a.title")?.TextContent,
                Upvotes = div.QuerySelector("div.score.unvoted")?.GetAttribute("title"),
                Comments = div.QuerySelector("a.comments")?.TextContent,
                CommentsUrl = div.QuerySelector("a.comments")?.GetAttribute("href"),
                PostedAt = div.QuerySelector("time")?.GetAttribute("datetime"),
                PostedBy = div.QuerySelector("a.author")?.TextContent,
            })
            .Select(queried => new RedditTopLevelPostBrief(
                new(root, Guard.Argument(queried.PostUrl).NotEmpty()),
                Guard.Argument(queried.Title).NotEmpty(),
                long.Parse(queried.Upvotes.AsSpan()),
                long.Parse(queried.Comments.AsSpan()),
                new(queried.CommentsUrl),
                DateTimeOffset.Parse(queried.PostedAt.AsSpan()),
                new(Guard.Argument(queried.PostedBy).NotEmpty())
            ), IExceptionHandler.Handle((ex, item) => _logger.LogInformation(ex, "Failed to parse {RedditTopLevelPostBrief}", item)));
        foreach (var item in queriedContent)
        {
            await _publisher.PublishAsync(item, stoppingToken);
        }
    }
}

sealed class RedditTopLevelPostCommentsSpider : IDataflowHandler<RedditTopLevelPostBrief>
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
    private readonly ILogger<RedditTopLevelPostCommentsSpider> _logger;
    private readonly IDataflowPublisher<RedditComment> _publisher;

    public RedditTopLevelPostCommentsSpider(IAngleSharpBrowserPageLoader browserPageLoader, ILogger<RedditTopLevelPostCommentsSpider> logger, IDataflowPublisher<RedditComment> publisher)
    {
        _browserPageLoader = browserPageLoader;
        _logger = logger;
        _publisher = publisher;
    }

    public async ValueTask HandleAsync(RedditTopLevelPostBrief message, CancellationToken cancellationToken = default)
    {
        var document = await _browserPageLoader.LoadAsync(message.CommentsUrl, cancellationToken);
        var queriedContent = document
            .QuerySelectorAll("div.commentarea > div.sitetable.nestedlisting div.comment > div.entry")
            .Select(div => new
            {
                ParentCommentUrl = div.ParentElement?.ParentElement?.ParentElement is { } childContainer &&
                    childContainer.ClassList.Contains("child") &&
                    childContainer.ParentElement is { } parentComment &&
                    parentComment.ClassList.Contains("comment")
                        ? parentComment.QuerySelector("div.flat-list.buttons > a.bylink")?.GetAttribute("href")
                        : message.CommentsUrl.ToString(),
                CommentUrl = div.QuerySelector("div.flat-list.buttons > a.bylink")?.GetAttribute("href"),
                HtmlText = div.QuerySelector("div.md")?.InnerHtml,
                PostedAt = div.QuerySelector("time")?.GetAttribute("datetime"),
                PostedBy = div.QuerySelector("a.author")?.TextContent,
            })
            .Select(queried => new RedditComment(
                new(queried.ParentCommentUrl),
                new(queried.CommentUrl),
                Guard.Argument(queried.HtmlText).NotEmpty(),
                DateTimeOffset.Parse(queried.PostedAt.AsSpan()),
                new(Guard.Argument(queried.PostedBy).NotEmpty())
            ), IExceptionHandler.Handle((ex, item) => _logger.LogInformation(ex, "Failed to parse {RedditComment}", item)));
        foreach (var comment in queriedContent)
        {
            await _publisher.PublishAsync(comment, cancellationToken);
        }
    }
}