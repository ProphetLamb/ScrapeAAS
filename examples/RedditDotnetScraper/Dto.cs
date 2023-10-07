using System.ComponentModel.DataAnnotations;

sealed class RedditSubredditDto
{
    [Required]
    public string? Url { get; set; }
}
sealed class RedditUserIdDto
{
    [Required]
    public string? Id { get; set; }
}
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
