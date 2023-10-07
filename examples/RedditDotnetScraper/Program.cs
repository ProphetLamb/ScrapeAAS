using ScrapeAAS;
using RedditDotnetScraper;
using Dawn;
using AngleSharp.Dom;
using Microsoft.EntityFrameworkCore;
using AutoMapper;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddAutoMapper(typeof(Program))
    .AddScrapeAAS(new() { MessagePipe = options => options.InstanceLifetime = MessagePipe.InstanceLifetime.Scoped })
    .AddHostedService<RedditSubredditCrawler>()
    .AddDataFlow<RedditPostSpider>()
    .AddDataFlow<RedditCommentsSpider>()
    .AddDataFlow<RedditSqliteSink>()
    .AddDbContext<RedditPostSqliteContext>(options => options.UseSqlite("Data Source=reddit.db"), ServiceLifetime.Singleton, ServiceLifetime.Singleton);
var app = builder.Build();
app.Run();

sealed record RedditSubreddit(Url Url);
sealed record RedditUserId(string Id);
sealed record RedditTopLevelPost(Url PostUrl, string Title, long Upvotes, long Comments, Url CommentsUrl, DateTimeOffset PostedAt, RedditUserId PostedBy);
sealed record RedditPostComment(Url PostUrl, Url? ParentCommentUrl, Url CommentUrl, string HtmlText, DateTimeOffset PostedAt, RedditUserId PostedBy);

/// <summary>
/// Periodically crawls the /r/dotnet subreddit.
/// </summary>
sealed class RedditSubredditCrawler : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<RedditPostSpider> _logger;

    public RedditSubredditCrawler(IServiceScopeFactory serviceScopeFactory, ILogger<RedditPostSpider> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Crawling /r/dotnet");
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var publisher = scope.ServiceProvider.GetRequiredService<IDataflowPublisher<RedditSubreddit>>();
                var context = scope.ServiceProvider.GetRequiredService<RedditPostSqliteContext>();
                await publisher.PublishAsync(new(new("dotnet")), stoppingToken);
                await context.SaveChangesAsync(stoppingToken);
            }
            _logger.LogInformation("Crawling complete");
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}

/// <summary>
/// Scrapes the top level posts from the subreddit.
/// </summary>
sealed class RedditPostSpider : IDataflowHandler<RedditSubreddit>
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
    private readonly ILogger _logger;
    private readonly IDataflowPublisher<RedditTopLevelPost> _publisher;

    public RedditPostSpider(IAngleSharpBrowserPageLoader browserPageLoader, ILogger<RedditPostSpider> logger, IDataflowPublisher<RedditTopLevelPost> publisher)
    {
        _browserPageLoader = browserPageLoader;
        _logger = logger;
        _publisher = publisher;
    }

    public async ValueTask HandleAsync(RedditSubreddit message, CancellationToken cancellationToken = default)
    {
        await ParseRedditTopLevelPosts(message, cancellationToken);
    }

    private async Task ParseRedditTopLevelPosts(RedditSubreddit subreddit, CancellationToken stoppingToken)
    {
        Url root = new("https://old.reddit.com");
        Url source = subreddit.Url;
        _logger.LogInformation("Parsing top level posts from {RedditSubreddit}", subreddit);
        var document = await _browserPageLoader.LoadAsync(source, stoppingToken);
        _logger.LogInformation("Request complete");
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
        _logger.LogInformation("Parsing complete");
    }
}

/// <summary>
/// Scrapes the comments from a top level post, and related them to their parent comments.
/// </summary>
sealed class RedditCommentsSpider : IDataflowHandler<RedditTopLevelPost>
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
    private readonly ILogger<RedditCommentsSpider> _logger;
    private readonly IDataflowPublisher<RedditPostComment> _publisher;

    public RedditCommentsSpider(IAngleSharpBrowserPageLoader browserPageLoader, ILogger<RedditCommentsSpider> logger, IDataflowPublisher<RedditPostComment> publisher)
    {
        _browserPageLoader = browserPageLoader;
        _logger = logger;
        _publisher = publisher;
    }

    public async ValueTask HandleAsync(RedditTopLevelPost message, CancellationToken cancellationToken = default)
    {
        await ParseRedditComments(message, cancellationToken);
    }

    private async Task ParseRedditComments(RedditTopLevelPost message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Parsing comments from {RedditTopLevelPost}", message);
        var document = await _browserPageLoader.LoadAsync(message.CommentsUrl, cancellationToken);
        _logger.LogInformation("Request complete");
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
        _logger.LogInformation("Parsing complete");
    }
}

/// <summary>
/// Inserts <see cref="RedditPost"/>s into a SQLite database.
/// </summary>
sealed class RedditSqliteSink : IDataflowHandler<RedditTopLevelPost>, IDataflowHandler<RedditPostComment>
{
    private readonly RedditPostSqliteContext _context;
    private readonly IMapper _mapper;

    public RedditSqliteSink(RedditPostSqliteContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async ValueTask HandleAsync(RedditTopLevelPost message, CancellationToken cancellationToken = default)
    {
        var messageDto = _mapper.Map<RedditTopLevelPostDto>(message);
        await _context.Database.EnsureCreatedAsync(cancellationToken);
        await _context.TopLevelPosts.AddAsync(messageDto, cancellationToken);
    }

    public async ValueTask HandleAsync(RedditPostComment message, CancellationToken cancellationToken = default)
    {
        var messageDto = _mapper.Map<RedditPostCommentDto>(message);
        await _context.Database.EnsureCreatedAsync(cancellationToken);
        await _context.Comments.AddAsync(messageDto, cancellationToken);
    }
}

/// <summary>
/// Represents the SQLite database context for the Reddit scraper.
/// </summary>
sealed class RedditPostSqliteContext : DbContext
{
    public RedditPostSqliteContext(DbContextOptions<RedditPostSqliteContext> options) : base(options) { }

    public DbSet<RedditTopLevelPostDto> TopLevelPosts { get; set; } = default!;
    public DbSet<RedditPostCommentDto> Comments { get; set; } = default!;
}