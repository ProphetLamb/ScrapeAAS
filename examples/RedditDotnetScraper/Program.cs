using System.Collections.Immutable;
using ScrapeAAS;
using ScrapeAAS.Contracts;
using ScrapeAAS.AngleSharp;
using RedditDotnetScraper;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddHttpClientStaticPageLoader()
    .AddPuppeteerBrowserPageLoader()
    .AddAngleSharpPageLoader()
    .AddHostedService<RedditSubRedditCommentBriefProvider>()
    .AddDataFlow()
    .AddDataFlowHandler<RedditPostBrief, RedditCommentProvider>();
var app = builder.Build();
app.Run();

sealed record RedditUserId(string Id);

sealed record RedditPost(RedditPostBrief Brief, RedditComment Comment);
sealed record RedditPostBrief(Uri PostUrl, string Title, long Upvotes, long Comments, Uri CommentsUrl, DateTimeOffset PostedAt, RedditUserId PostedBy);
sealed record RedditComment(Uri PostUrl, string HtmlText, DateTimeOffset PostedAt, RedditUserId PostedBy, ImmutableArray<RedditComment> Replies);

sealed class RedditSubRedditCommentBriefProvider : BackgroundService
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
    private readonly ILogger<RedditSubRedditCommentBriefProvider> _logger;
    private readonly IDataflowPublisher<RedditPostBrief> _publisher;

    public RedditSubRedditCommentBriefProvider(IAngleSharpBrowserPageLoader browserPageLoader, ILogger<RedditSubRedditCommentBriefProvider> logger, IDataflowPublisher<RedditPostBrief> publisher)
    {
        _browserPageLoader = browserPageLoader;
        _logger = logger;
        _publisher = publisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Uri root = new("https://old.reddit.com");
        Uri source = new(root, "/r/dotnet/");
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
            .Select(queried => new RedditPostBrief(
                new(root, queried.PostUrl),
                queried.Title.AssertNotEmpty(),
                long.Parse(queried.Upvotes.AssertNotEmpty()),
                long.Parse(queried.Comments.AssertNotEmpty()),
                new Uri(root, queried.CommentsUrl.AssertNotEmpty()),
                DateTimeOffset.Parse(queried.PostedAt.AssertNotEmpty()),
                new(queried.PostedBy.AssertNotEmpty())
            ), IExceptionHandler.Handle(ex => _logger.LogInformation(ex, "Failed to parse element")));
        foreach (var item in queriedContent)
        {
            await _publisher.PublishAsync(item, stoppingToken);
        }
    }
}

sealed class RedditCommentProvider : IDataflowHandler<RedditPostBrief>
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
    private readonly ILogger<RedditCommentProvider> _logger;
    private readonly IDataflowPublisher<RedditComment> _publisher;

    public RedditCommentProvider(IAngleSharpBrowserPageLoader browserPageLoader, ILogger<RedditCommentProvider> logger, IDataflowPublisher<RedditComment> publisher)
    {
        _browserPageLoader = browserPageLoader;
        _logger = logger;
        _publisher = publisher;
    }

    public async ValueTask HandleAsync(RedditPostBrief message, CancellationToken cancellationToken = default)
    {
        var document = await _browserPageLoader.LoadAsync(message.CommentsUrl, cancellationToken);
        var queriedContent = document
            .QuerySelectorAll("div.commentarea > div.sitetable.nestedlisting > div.comment")
            .Select(div => new
            {
                HtmlText = div.QuerySelector("div.md")?.InnerHtml,
                PostedAt = div.QuerySelector("time")?.GetAttribute("datetime"),
                PostedBy = div.QuerySelector("a.author")?.TextContent,
            })
            .Select(queried => new RedditComment(
                message.PostUrl,
                queried.HtmlText.AssertNotEmpty(),
                DateTimeOffset.Parse(queried.PostedAt.AssertNotEmpty()),
                new(queried.PostedBy.AssertNotEmpty()),
                ImmutableArray<RedditComment>.Empty
            ), IExceptionHandler.Handle(ex => _logger.LogInformation(ex, "Failed to parse element")));
        foreach (var comment in queriedContent)
        {
            await _publisher.PublishAsync(comment, cancellationToken);
        }
    }
}