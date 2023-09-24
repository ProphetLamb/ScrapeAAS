using ScrapeAAS;
using RedditDotnetScraper;
using Dawn;
using AngleSharp.Dom;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddScrapeAAS()
    .AddHostedService<TopLevelRedditPostSpider>()
    .AddDataFlow<RedditPostCommentsSpider>()
    .AddHostedService<RedditPostSqliteSink>().AddDataFlow<RedditPostSqliteSink>(useExistingSingleton: true)
    .AddDbContext<RedditPostSqliteContext>(options => options.UseSqlite("Data Source=reddit.db"), ServiceLifetime.Singleton, ServiceLifetime.Singleton);
var app = builder.Build();
app.Run();

// Scraped data models
sealed record RedditUserId(string Id);
sealed record RedditTopLevelPost(Url PostUrl, string Title, long Upvotes, long Comments, Url CommentsUrl, DateTimeOffset PostedAt, RedditUserId PostedBy);
sealed record RedditPostComment(Url PostUrl, Url? ParentCommentUrl, Url CommentUrl, string HtmlText, DateTimeOffset PostedAt, RedditUserId PostedBy);

/// <summary>
/// Scrapes the top level posts from the /r/dotnet subreddit.
/// </summary>
sealed class TopLevelRedditPostSpider : BackgroundService
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
    private readonly ILogger<TopLevelRedditPostSpider> _logger;
    private readonly IDataflowPublisher<RedditTopLevelPost> _publisher;

    public TopLevelRedditPostSpider(IAngleSharpBrowserPageLoader browserPageLoader, ILogger<TopLevelRedditPostSpider> logger, IDataflowPublisher<RedditTopLevelPost> publisher)
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
            .AsParallel()
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
            .Select(queried => new RedditTopLevelPost(
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

/// <summary>
/// Scrapes the comments from a top level post, and related them to their parent comments.
/// </summary>
sealed class RedditPostCommentsSpider : IDataflowHandler<RedditTopLevelPost>
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
    private readonly ILogger<RedditPostCommentsSpider> _logger;
    private readonly IDataflowPublisher<RedditPostComment> _publisher;

    public RedditPostCommentsSpider(IAngleSharpBrowserPageLoader browserPageLoader, ILogger<RedditPostCommentsSpider> logger, IDataflowPublisher<RedditPostComment> publisher)
    {
        _browserPageLoader = browserPageLoader;
        _logger = logger;
        _publisher = publisher;
    }

    public async ValueTask HandleAsync(RedditTopLevelPost message, CancellationToken cancellationToken = default)
    {
        var document = await _browserPageLoader.LoadAsync(message.CommentsUrl, cancellationToken);
        var queriedContent = document
            .QuerySelectorAll("div.commentarea > div.sitetable.nestedlisting div.comment > div.entry")
            .AsParallel()
            .Select(div => new
            {
                ParentCommentUrl = div.ParentElement?.ParentElement?.ParentElement is { } childContainer &&
                    childContainer.ClassList.Contains("child") &&
                    childContainer.ParentElement is { } parentComment &&
                    parentComment.ClassList.Contains("comment")
                        ? parentComment.QuerySelector("div.flat-list.buttons > a.bylink")?.GetAttribute("href")
                        : null,
                CommentUrl = div.QuerySelector("div.flat-list.buttons > a.bylink")?.GetAttribute("href"),
                HtmlText = div.QuerySelector("div.md")?.InnerHtml,
                PostedAt = div.QuerySelector("time")?.GetAttribute("datetime"),
                PostedBy = div.QuerySelector("a.author")?.TextContent,
            })
            .Select(queried => new RedditPostComment(
                new(message.PostUrl),
                queried.ParentCommentUrl is null ? new(queried.ParentCommentUrl) : null,
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

/// <summary>
/// Inserts <see cref="RedditPost"/>s into a SQLite database.
/// </summary>
sealed class RedditPostSqliteSink : BackgroundService, IDataflowHandler<RedditTopLevelPost>, IDataflowHandler<RedditPostComment>
{
    private readonly RedditPostSqliteContext _context;

    public RedditPostSqliteSink(RedditPostSqliteContext context)
    {
        _context = context;
    }

    public async ValueTask HandleAsync(RedditTopLevelPost message, CancellationToken cancellationToken = default)
    {
        await _context.Database.EnsureCreatedAsync(cancellationToken);
        await _context.TopLevelPosts.AddAsync(message, cancellationToken);
    }

    public async ValueTask HandleAsync(RedditPostComment message, CancellationToken cancellationToken = default)
    {
        await _context.Database.EnsureCreatedAsync(cancellationToken);
        await _context.Comments.AddAsync(message, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            await _context.SaveChangesAsync(stoppingToken);
        }
    }
}

sealed class RedditPostSqliteContext : DbContext
{
    public DbSet<RedditTopLevelPost> TopLevelPosts { get; set; } = default!;
    public DbSet<RedditPostComment> Comments { get; set; } = default!;
}