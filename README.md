# Scrape as a service

ScrapeAAS integrates existing packages and ASP.NET features into a toolstack enabling you, the developer, to design your scraping service using a fammilar environment.

## Quickstart

Add `ASP.NET Hosting`, `ScrapeAAS`, a validator of your choice (here [Dawn.Guard](https://github.com/safakgur/guard) RIP), and a object mapper of your choice (here [AutoMapper](https://automapper.org/)), and the database/messagequeue you feel most comftable with (here [EFcore](https://learn.microsoft.com/en-us/ef/core/get-started/overview/first-app?tabs=netcore-cli) with SQLite).

```bash
dotnet add package Microsoft.Extensions.Hosting
dotnet add package ScrapeAAS
dotnet add package Dawn.Guard
dotnet add package AutoMapper.Extensions.Microsoft.DependencyInjection
```

**[Full example](./examples/RedditDotnetScraper/) of scraping the [r/dotnet subreddit](https://old.reddit.com/r/dotnet).**

Create a crawler, a that service periodically triggers scraping

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services
  .AddAutoMapper()
  .AddScrapeAAS()
  .AddHostedService<RedditSubredditCrawler>()
  .AddDataflow<RedditPostSpider>()
  .AddDataflow<RedditSqliteSink>()

sealed class RedditSubredditCrawler : BackgroundService {
  private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
  private readonly IDataflowPublisher<RedditPost> _publisher;
  ...
  protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
    ... execute service scope periotically
  }

  private async Task CrawlAsync(IDataflowPublisher<RedditSubreddit> publisher, CancellationToken stoppingToken)
  {
    _logger.LogInformation("Crawling /r/dotnet");
    await publisher.PublishAsync(new("dotnet", new("https://old.reddit.com/r/dotnet")), stoppingToken);
    _logger.LogInformation("Crawling complete");
  }
}
```

Implement your spiders, services that collect, and normalize data.

```csharp

sealed class RedditPostSpider : IDataflowHandler<RedditSubreddit> {
  private readonly IAngleSharpBrowserPageLoader _browserPageLoader;
  private readonly IDataflowPublisher<RedditComment> _publisher;
  ...

  private async Task ParseRedditTopLevelPosts(RedditSubreddit subreddit, CancellationToken stoppingToken)
  {
    Url root = new("https://old.reddit.com/");
    _logger.LogInformation("Parsing top level posts from {RedditSubreddit}", subreddit);
    var document = await _browserPageLoader.LoadAsync(subreddit.Url, stoppingToken);
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
        Regex.Match(queried.Comments ?? "", "^\\d+") is { Success: true } commentCount ? long.Parse(commentCount.Value) : 0,
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
```

Add a sink, a service that commits the scraped data disk/network.

```csharp
sealed class RedditSqliteSink : IAsyncDisposable, IDataflowHandler<RedditSubreddit>, IDataflowHandler<RedditPost>
{
  private readonly RedditPostSqliteContext _context;
  private readonly IMapper _mapper;
  ...
  public async ValueTask DisposeAsync()
  {
    await _context.Database.EnsureCreatedAsync();
    await _context.SaveChangesAsync();
  }

  public async ValueTask HandleAsync(RedditSubreddit message, CancellationToken cancellationToken = default)
  {
    var messageDto = _mapper.Map<RedditSubredditDto>(message);
    await _context.Database.EnsureCreatedAsync(cancellationToken);
    await _context.Subreddits.AddAsync(messageDto, cancellationToken);
  }

  public async ValueTask HandleAsync(RedditPost message, CancellationToken cancellationToken = default)
  {
    var messageDto = _mapper.Map<RedditPostDto>(message);
    if (await _context.Users.FindAsync(new object[] { message.PostedBy.Id }, cancellationToken) is { } existingUser)
    {
      messageDto.PostedById = existingUser.Id;
      messageDto.PostedBy = existingUser;
    }
    await _context.Database.EnsureCreatedAsync(cancellationToken);
    await _context.Posts.AddAsync(messageDto, cancellationToken);
  }
}
```

## Why not [WebReaper](https://github.com/pavlovtech/WebReaper) or [DotnetSpider](https://github.com/dotnetcore/DotnetSpider)?

I have tried both toolstacks, and found them wanting. So I tried to make it better by delegating as much work as reasonable to existing projects.

In addition to my own goals; from evaluating both libraries I wish to keep all thier pros, and discard all their cons.
The verbocity of this library sits comtably between WebReaper and DotnetSpider, but more towards the DotnetSpider end of things.

- Integration into ASP.NET Hosting.
- No dependencies at the core of the project. Instead package a reasonable set of addons by default.
- Use and expose integrated NuGet packages in addons when possible to allow develops to benefit form existing ecosystems.

### Evaluation of [DotnetSpider](https://github.com/dotnetcore/DotnetSpider)

The overall data flow in `ScrapeAAS` is adopted from `DotnetSpider`: Crawler --> Spider --> Sink .

- Pro: Pub/Sub event handling for decoupled data flow.
- Pro: Easy extendibility by tapping events.
- Con: Terrible debugging experience using model annotations.
- Con: Smelly `dynamic` riddeled design when storing to a database.
- Con: Retry policies missing.
- Con: Much boilerplate nessessary.

### Evaluation of [WebReaper](https://github.com/pavlovtech/WebReaper)

The [Puppeteer](https://pptr.dev/) browser handling is a mixture of the [lifetime tracking http handler](https://source.dot.net/#Microsoft.Extensions.Http/DefaultHttpClientFactory.cs) and the [WebReaper Puppeteer integration](https://github.com/pavlovtech/WebReaper/blob/master/WebReaper/Core/Loaders/Concrete/PuppeteerPageLoader.cs).

- Pro: Simple declarative builder API. No boilderplate needed.
- Pro: Easy extendibility by implementing interfaces.
- Pro: Puppeteer browser.
- Con: Unable to control data flow.
- Con: Unable to parse data.
- Con: No ASP.NET or **any** DI integration possible.
- Con: Dependencies for optional extendibilites, such as `Redis`, `MySql`, `RabbitMq`, are always included in the package.
