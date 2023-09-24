using ScrapeAAS;
using RedditDotnetScraper;
using Dawn;
using AngleSharp.Dom;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddScrapeAAS()
    .AddHostedService<TopLevelRedditPostSpider>()
    .AddDataFlow<RedditPostCommentsSpider>()
    .AddHostedService<RedditPostRelationalAggegator>().AddDataFlow<RedditPostRelationalAggegator>(useExistingSingleton: true);
var app = builder.Build();
app.Run();

// Scraped data models
sealed record RedditUserId(string Id);
sealed record RedditTopLevelPost(Url PostUrl, string Title, long Upvotes, long Comments, Url CommentsUrl, DateTimeOffset PostedAt, RedditUserId PostedBy);
sealed record RedditPostComment(Url PostUrl, Url? ParentCommentUrl, Url CommentUrl, string HtmlText, DateTimeOffset PostedAt, RedditUserId PostedBy);
// EF Core relational models
sealed record RedditCommentTree(RedditPostComment Comment, List<RedditCommentTree> Children);
sealed record RedditPost(RedditTopLevelPost TopLevel, List<RedditCommentTree> Comments);

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
/// Inverts the relational structure of the scraped data from child n->1 parent to parent 1->n children.
/// Debounced publishes posts that have not received comments for a while.
/// </summary>
sealed class RedditPostRelationalAggegator : BackgroundService, IDataflowHandler<RedditTopLevelPost>, IDataflowHandler<RedditPostComment>
{
    private readonly IDataflowPublisher<RedditPost> _publisher;
    private readonly ILogger<RedditPostRelationalAggegator> _logger;
    private readonly ConcurrentDictionary<Url, (RedditPost Post, long InsertCommentMissCounter)> _postWithCounterByUrl = new();
    private readonly ConcurrentBag<RedditPostComment> _headlessComments = new();

    public RedditPostRelationalAggegator(IDataflowPublisher<RedditPost> publisher, ILogger<RedditPostRelationalAggegator> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public ValueTask HandleAsync(RedditTopLevelPost message, CancellationToken cancellationToken = default)
    {
        _postWithCounterByUrl.TryAdd(message.CommentsUrl, (new(message, new()), 0));
        return default;
    }

    public ValueTask HandleAsync(RedditPostComment message, CancellationToken cancellationToken = default)
    {
        _headlessComments.Add(message);
        return default;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            InsertHeadlessComments();
            await PublishInactivePosts(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }


    private async Task PublishInactivePosts(CancellationToken stoppingToken)
    {
        foreach (var (post, commentMissCounter) in _postWithCounterByUrl.Values)
        {
            if (commentMissCounter > 100)
            {
                await PublishPost(post, stoppingToken).ConfigureAwait(false);
            }
        }

        async Task PublishPost(RedditPost post, CancellationToken stoppingToken)
        {
            if (_postWithCounterByUrl.TryRemove(post.TopLevel.CommentsUrl, out var postWithCounter))
            {
                _logger.LogWarning("Post {RedditTopLevelPost} has {CommentMissCounter} misses, publishing", post.TopLevel, postWithCounter.InsertCommentMissCounter);
                await _publisher.PublishAsync(postWithCounter.Post, stoppingToken);
            }
        }
    }

    private void InsertHeadlessComments()
    {
        // drain the headless comments, and attempt to insert them into the post
        List<RedditPostComment> failedComments = new();
        while (_headlessComments.TryTake(out var headlessComment))
        {
            if (!_postWithCounterByUrl.TryGetValue(headlessComment.PostUrl, out var postWithCounter))
            {
                failedComments.Add(headlessComment);
                continue;
            }
            if (!InsertComment(postWithCounter.Post, headlessComment))
            {
                IncrementInsertCommentMissCounter(headlessComment, postWithCounter);
                failedComments.Add(headlessComment);
            }
            else
            {
                ResetInsertCommentMissCounter(headlessComment, postWithCounter);
            }
        }

        // requeue the failed comments
        foreach (var comment in failedComments)
        {
            _headlessComments.Add(comment);
        }

        void IncrementInsertCommentMissCounter(RedditPostComment headlessComment, (RedditPost Post, long InsertCommentMissCounter) postWithCounter)
        {
            _postWithCounterByUrl.AddOrUpdate(headlessComment.PostUrl, (postWithCounter.Post, postWithCounter.InsertCommentMissCounter + 1), (_, postWithCounter) => (postWithCounter.Post, postWithCounter.InsertCommentMissCounter + 1));
        }

        void ResetInsertCommentMissCounter(RedditPostComment headlessComment, (RedditPost Post, long InsertCommentMissCounter) postWithCounter)
        {
            _postWithCounterByUrl.AddOrUpdate(headlessComment.PostUrl, (postWithCounter.Post, 0), (_, postWithCounter) => (postWithCounter.Post, 0));
        }
    }

    private bool InsertComment(RedditPost post, RedditPostComment insertComment)
    {
        // insertComments are linked by child.ParentCommentUrl == parent.CommentUrl
        // insertComments without a parent are top level comments
        if (insertComment.ParentCommentUrl is null)
        {
            post.Comments.Add(new(insertComment, new()));
            return true;
        }
        else
        {
            // find the parent comment
            var parentComment = post.Comments
                .SelectMany(commentTree => commentTree.Children)
                .FirstOrDefault(commentTree => commentTree.Comment.CommentUrl == insertComment.ParentCommentUrl);
            if (parentComment is { })
            {
                parentComment.Children.Add(new(insertComment, new()));
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Inserts <see cref="RedditPost"/>s into a SQLite database.
/// </summary>
sealed class RedditPostSqliteSink : IDataflowHandler<RedditPost>
{
    private readonly RedditPostSqliteContext _context;

    public RedditPostSqliteSink(RedditPostSqliteContext context)
    {
        _context = context;
    }

    public async ValueTask HandleAsync(RedditPost message, CancellationToken cancellationToken = default)
    {
        await _context.Database.EnsureCreatedAsync(cancellationToken);
        await _context.Posts.AddAsync(message, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

sealed class RedditPostSqliteContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=reddit_dotnet.db");
    }

    public DbSet<RedditPost> Posts { get; set; } = default!;
}