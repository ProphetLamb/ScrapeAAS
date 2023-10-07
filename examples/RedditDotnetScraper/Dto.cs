using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

[PrimaryKey(nameof(Url)), Table("Subreddits")]
sealed class RedditSubredditDto
{
    [Required]
    public string? Url { get; set; }
}
[PrimaryKey(nameof(Id)), Table("Users")]
sealed class RedditUserIdDto
{
    [Required]
    public string? Id { get; set; }
}

[PrimaryKey(nameof(PostUrl)), Table("Posts")]
sealed class RedditTopLevelPostDto
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
    public RedditUserIdDto? PostedBy { get; set; }
}
[PrimaryKey(nameof(CommentUrl)), Table("Comments")]
sealed class RedditPostCommentDto
{
    public string? PostUrl { get; set; }
    public string? ParentCommentUrl { get; set; }
    public string? CommentUrl { get; set; }
    public string? HtmlText { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    [Required]
    public RedditUserIdDto? PostedBy { get; set; }
}
