using System.Collections.Immutable;
using ScrapeAAS;
using ScrapeAAS.Contracts;
using ScrapeAAS.AngleSharp;
using RedditDotnetScraper;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddHttpClientStaticPageLoader()
    .AddPuppeteerBrowserPageLoader()
    .AddAngleSharpPageLoader();
var app = builder.Build();
app.Run();

sealed record RedditUserId(string Id);

sealed record RedditSubRedditCommentBrief(Uri PostUrl, string Title, long Upvotes, long Comments, Uri CommentsUrl, DateTimeOffset PostedAt, RedditUserId PostedBy);
sealed record RedditComment(Uri PostUrl, string HtmlText, DateTimeOffset PostedAt, RedditUserId PostedBy, ImmutableArray<RedditComment> Replies);
[Dataflow]
sealed record RedditSubRedditCommentBriefProvider(IAngleSharpBrowserPageLoader BrowserPageLoader, ILogger Logger) : IDataflowProvider<RedditSubRedditCommentBrief>
{
    public async Task ExecuteAsync(IDataSourceInflator<RedditSubRedditCommentBrief> inflator, CancellationToken cancellationToken = default)
    {
        Uri root = new("https://old.reddit.com");
        Uri source = new(root, "/r/dotnet/");
        var document = await BrowserPageLoader.LoadAsync(source, cancellationToken);
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
            .Select(queried => new RedditSubRedditCommentBrief(
                new(root, queried.PostUrl),
                queried.Title.AssertNotEmpty(),
                long.Parse(queried.Upvotes.AssertNotEmpty()),
                long.Parse(queried.Comments.AssertNotEmpty()),
                new Uri(root, queried.CommentsUrl.AssertNotEmpty()),
                DateTimeOffset.Parse(queried.PostedAt.AssertNotEmpty()),
                new(queried.PostedBy.AssertNotEmpty())
            ), IExceptionHandler.Handle(ex => Logger.LogInformation(ex, "Failed to parse element")));
        foreach (var item in queriedContent)
        {
            await inflator.AddAsync(item, cancellationToken);
        }
    }
}

[Dataflow]
sealed record RedditCommentProvider(IAngleSharpBrowserPageLoader BrowserPageLoader, ILogger Logger, IDataflowReader<RedditSubRedditCommentBrief> DataflowReader) : IDataflowProvider<RedditComment>
{
    public async Task ExecuteAsync(IDataSourceInflator<RedditComment> inflator, CancellationToken cancellationToken = default)
    {

        while (!cancellationToken.IsCancellationRequested)
        {
            var brief = await DataflowReader.ReadAsync(cancellationToken);
            var document = await BrowserPageLoader.LoadAsync(brief.CommentsUrl, cancellationToken);
            var queriedContent = document
                .QuerySelectorAll("div.commentarea > div.sitetable.nestedlisting > div.comment")
                .Select(div => new
                {
                    HtmlText = div.QuerySelector("div.md")?.InnerHtml,
                    PostedAt = div.QuerySelector("time")?.GetAttribute("datetime"),
                    PostedBy = div.QuerySelector("a.author")?.TextContent,
                })
                .Select(queried => new RedditComment(
                    brief.PostUrl,
                    queried.HtmlText.AssertNotEmpty(),
                    DateTimeOffset.Parse(queried.PostedAt.AssertNotEmpty()),
                    new(queried.PostedBy.AssertNotEmpty()),
                    ImmutableArray<RedditComment>.Empty
                ), IExceptionHandler.Handle(ex => Logger.LogInformation(ex, "Failed to parse element")));
            foreach (var comment in queriedContent)
            {
                await inflator.AddAsync(comment, cancellationToken);
            }
        }
    }
}