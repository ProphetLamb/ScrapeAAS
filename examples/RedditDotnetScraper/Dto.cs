using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AngleSharp.Dom;
using AutoMapper;
using Microsoft.EntityFrameworkCore;

[PrimaryKey(nameof(Name)), Table("Subreddits")]
internal sealed class RedditSubredditDto
{
    [Required]
    public string? Name { get; set; }
    [Required]
    public string? Url { get; set; }
}
[PrimaryKey(nameof(Id)), Table("Users")]
internal sealed class RedditUserDto
{
    [Required]
    public string? Id { get; set; }
}

[PrimaryKey(nameof(PostUrl)), Table("Posts")]
internal sealed class RedditPostDto
{
    [Required]
    public string? PostUrl { get; set; }
    [Required]
    public string? Title { get; set; }
    public long Upvotes { get; set; }
    public long Comments { get; set; }
    [Required]
    public string? CommentsUrl { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    [Required]
    public string? PostedById { get; set; }
    [Required, ForeignKey(nameof(PostedById))]
    public RedditUserDto? PostedBy { get; set; }
}
[PrimaryKey(nameof(CommentUrl)), Table("Comments")]
internal sealed class RedditCommentDto
{
    public string? PostUrl { get; set; }
    public string? ParentCommentUrl { get; set; }
    public string? CommentUrl { get; set; }
    public string? HtmlText { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    [Required]
    public string? PostedById { get; set; }
    [Required, ForeignKey(nameof(PostedById))]
    public RedditUserDto? PostedBy { get; set; }
}

internal sealed class UrlStringConverter : ITypeConverter<Url, string?>, ITypeConverter<string?, Url>
{
    public Url Convert(string? source, Url destination, ResolutionContext context)
    {
        return new(source ?? "");
    }

    public string Convert(Url source, string? destination, ResolutionContext context)
    {
        return source?.ToString() ?? "";
    }
}
