using ScrapeAAS;
using RedditDotnetScraper;
using Dawn;
using AngleSharp.Dom;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using System.Text.RegularExpressions;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddAutoMapper(options =>
    {
        options.CreateMap<Url, string?>().ConvertUsing<UrlStringConverter>();
        options.CreateMap<string?, Url>().ConvertUsing<UrlStringConverter>();
        _ = options.CreateMap<RedditSubreddit, RedditSubredditDto>();
        _ = options.CreateMap<RedditUser, RedditUserDto>();
        _ = options.CreateMap<RedditPost, RedditPostDto>();
        _ = options.CreateMap<RedditComment, RedditCommentDto>();
    }, typeof(Program))
    .AddDbContext<RedditPostSqliteContext>(options => options.UseSqlite("Data Source=reddit.db"))
    .AddScrapeAAS(config => config
        .UseDefaultConfiguration()
        .AddDataFlow<RedditPostSpider>()
        .AddDataFlow<RedditCommentsSpider>()
        .AddDataFlow<RedditSqliteSink>()
    )
    .AddHostedService<RedditSubredditCrawler>();
var app = builder.Build();
app.Run();

internal sealed record RedditSubreddit(string Name, Url Url);

internal sealed record RedditUser(string Id);

internal sealed record RedditPost(Url PostUrl, string Title, long Upvotes, long Comments, Url CommentsUrl, DateTimeOffset PostedAt, RedditUser PostedBy);

internal sealed record RedditComment(Url PostUrl, Url? ParentCommentUrl, Url CommentUrl, string HtmlText, DateTimeOffset PostedAt, RedditUser PostedBy);

/// <summary>
/// Periodically crawls the /r/dotnet subreddit.
/// </summary>
internal sealed class RedditSubredditCrawler(IServiceScopeFactory serviceScopeFactory, ILogger<RedditPostSpider> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<RedditPostSpider> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using (var scope = _serviceScopeFactory.CreateAsyncScope())
            {
                var publisher = scope.ServiceProvider.GetRequiredService<IDataflowPublisher<RedditSubreddit>>();
                await CrawlAsync(publisher, stoppingToken);
            }
            await Task.Delay(TimeSpan.FromHours(3), stoppingToken);
        }
    }

    private async Task CrawlAsync(IDataflowPublisher<RedditSubreddit> publisher, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Crawling /r/dotnet");
        await publisher.PublishAsync(new("dotnet", new("https://old.reddit.com/r/dotnet")), stoppingToken);
        _logger.LogInformation("Crawling complete");
    }
}

/// <summary>
/// Scrapes the top level posts from the subreddit.
/// </summary>
internal sealed class RedditPostSpider(ILogger<RedditPostSpider> logger, IDataflowPublisher<RedditPost> publisher, IAngleSharpBrowserPageLoader browserPageLoader) : IDataflowHandler<RedditSubreddit>
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader = browserPageLoader;
    private readonly ILogger _logger = logger;
    private readonly IDataflowPublisher<RedditPost> _publisher = publisher;

    public async ValueTask HandleAsync(RedditSubreddit message, CancellationToken cancellationToken = default)
    {
        await ParseRedditTopLevelPosts(message, cancellationToken);
    }

    private async Task ParseRedditTopLevelPosts(RedditSubreddit subreddit, CancellationToken stoppingToken)
    {
        Url root = new("https://old.reddit.com/");
        _logger.LogInformation("Parsing top level posts from {RedditSubreddit}", subreddit);
        var document = await _browserPageLoader.LoadAsync(subreddit.Url, stoppingToken).ConfigureAwait(false);
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
            .Select(queried => new RedditPost(
                new(root, Guard.Argument(queried.PostUrl).NotEmpty()),
                Guard.Argument(queried.Title).NotEmpty(),
                long.Parse(queried.Upvotes.AsSpan()),
                Regex.Match(queried.Comments ?? "", @"^\d+") is { Success: true } commentCount ? long.Parse(commentCount.Value) : 0,
                new(queried.CommentsUrl),
                DateTimeOffset.Parse(queried.PostedAt.AsSpan()),
                new(Guard.Argument(queried.PostedBy).NotEmpty())
            ), IExceptionHandler.Handle((ex, item) => _logger.LogInformation(ex, "Failed to parse {RedditTopLevelPostBrief}", item)));
        foreach (var item in queriedContent)
        {
            await _publisher.PublishAsync(item, stoppingToken).ConfigureAwait(false);
        }
        _logger.LogInformation("Parsing complete");
    }
}

/// <summary>
/// Scrapes the comments from a top level post, and related them to their parent comments.
/// </summary>
internal sealed class RedditCommentsSpider(ILogger<RedditCommentsSpider> logger, IDataflowPublisher<RedditComment> publisher, IAngleSharpBrowserPageLoader browserPageLoader) : IDataflowHandler<RedditPost>
{
    private readonly IAngleSharpBrowserPageLoader _browserPageLoader = browserPageLoader;
    private readonly ILogger<RedditCommentsSpider> _logger = logger;
    private readonly IDataflowPublisher<RedditComment> _publisher = publisher;

    public async ValueTask HandleAsync(RedditPost message, CancellationToken cancellationToken = default)
    {
        await ParseRedditComments(message, cancellationToken);
    }

    private async Task ParseRedditComments(RedditPost message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Parsing comments from {RedditTopLevelPost}", message);
        var document = await _browserPageLoader.LoadAsync(message.CommentsUrl, cancellationToken).ConfigureAwait(false);
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
                        ? parentComment.QuerySelector("ul.flat-list.buttons a.bylink")?.GetAttribute("href")
                        : null,
                CommentUrl = div.QuerySelector("ul.flat-list.buttons a.bylink")?.GetAttribute("href"),
                HtmlText = div.QuerySelector("div.md")?.InnerHtml,
                PostedAt = div.QuerySelector("time")?.GetAttribute("datetime"),
                PostedBy = div.QuerySelector("a.author")?.TextContent,
            })
            .Select(queried => new RedditComment(
                new(message.PostUrl),
                queried.ParentCommentUrl is { } parentCommentUrl ? new(parentCommentUrl) : null,
                new(queried.CommentUrl),
                Guard.Argument(queried.HtmlText).NotEmpty(),
                DateTimeOffset.Parse(queried.PostedAt.AsSpan()),
                new(Guard.Argument(queried.PostedBy).NotEmpty())
            ),
            IExceptionHandler.Handle((ex, item) => _logger.LogInformation(ex, "Failed to parse {RedditComment}", item)));
        foreach (var comment in queriedContent)
        {
            await _publisher.PublishAsync(comment, cancellationToken).ConfigureAwait(false);
        }
        _logger.LogInformation("Parsing complete");
    }
}

/// <summary>
/// Inserts <see cref="RedditPost"/>s into a SQLite database.
/// </summary>
internal sealed class RedditSqliteSink(RedditPostSqliteContext context, IMapper mapper) : IAsyncDisposable, IDataflowHandler<RedditSubreddit>, IDataflowHandler<RedditPost>, IDataflowHandler<RedditComment>
{
    private readonly RedditPostSqliteContext _context = context;
    private readonly IMapper _mapper = mapper;

    public async ValueTask DisposeAsync()
    {
        _ = await _context.Database.EnsureCreatedAsync();
        _ = await _context.SaveChangesAsync();
    }

    public async ValueTask HandleAsync(RedditSubreddit message, CancellationToken cancellationToken = default)
    {
        var messageDto = _mapper.Map<RedditSubredditDto>(message);
        _ = await _context.Database.EnsureCreatedAsync(cancellationToken);
        _ = await _context.Subreddits.AddAsync(messageDto, cancellationToken);
    }

    public async ValueTask HandleAsync(RedditPost message, CancellationToken cancellationToken = default)
    {
        var messageDto = _mapper.Map<RedditPostDto>(message);
        if (await _context.Users.FindAsync(new object[] { message.PostedBy.Id }, cancellationToken) is { } existingUser)
        {
            messageDto.PostedById = existingUser.Id;
            messageDto.PostedBy = existingUser;
        }
        _ = await _context.Database.EnsureCreatedAsync(cancellationToken);
        _ = await _context.Posts.AddAsync(messageDto, cancellationToken);
    }

    public async ValueTask HandleAsync(RedditComment message, CancellationToken cancellationToken = default)
    {
        var messageDto = _mapper.Map<RedditCommentDto>(message);
        if (await _context.Users.FindAsync(new object[] { message.PostedBy.Id }, cancellationToken) is { } existingUser)
        {
            messageDto.PostedById = existingUser.Id;
            messageDto.PostedBy = existingUser;
        }
        _ = await _context.Database.EnsureCreatedAsync(cancellationToken);
        _ = await _context.Comments.AddAsync(messageDto, cancellationToken);
    }
}

/// <summary>
/// Represents the SQLite database context for the Reddit scraper.
/// </summary>
internal sealed class RedditPostSqliteContext(DbContextOptions<RedditPostSqliteContext> options) : DbContext(options)
{
    public DbSet<RedditSubredditDto> Subreddits { get; set; } = default!;
    public DbSet<RedditUserDto> Users { get; set; } = default!;
    public DbSet<RedditPostDto> Posts { get; set; } = default!;
    public DbSet<RedditCommentDto> Comments { get; set; } = default!;
}