using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RedditDotnetScraper.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.CreateTable(
            name: "Subreddits",
            columns: table => new
            {
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Url = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Subreddits", x => x.Name));

        _ = migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Users", x => x.Id));

        _ = migrationBuilder.CreateTable(
            name: "Comments",
            columns: table => new
            {
                CommentUrl = table.Column<string>(type: "TEXT", nullable: false),
                PostUrl = table.Column<string>(type: "TEXT", nullable: true),
                ParentCommentUrl = table.Column<string>(type: "TEXT", nullable: true),
                HtmlText = table.Column<string>(type: "TEXT", nullable: true),
                PostedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                PostedById = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_Comments", x => x.CommentUrl);
                _ = table.ForeignKey(
                    name: "FK_Comments_Users_PostedById",
                    column: x => x.PostedById,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateTable(
            name: "Posts",
            columns: table => new
            {
                PostUrl = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                Upvotes = table.Column<long>(type: "INTEGER", nullable: false),
                Comments = table.Column<long>(type: "INTEGER", nullable: false),
                CommentsUrl = table.Column<string>(type: "TEXT", nullable: false),
                PostedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                PostedById = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_Posts", x => x.PostUrl);
                _ = table.ForeignKey(
                    name: "FK_Posts_Users_PostedById",
                    column: x => x.PostedById,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_Comments_PostedById",
            table: "Comments",
            column: "PostedById");

        _ = migrationBuilder.CreateIndex(
            name: "IX_Posts_PostedById",
            table: "Posts",
            column: "PostedById");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "Comments");

        _ = migrationBuilder.DropTable(
            name: "Posts");

        _ = migrationBuilder.DropTable(
            name: "Subreddits");

        _ = migrationBuilder.DropTable(
            name: "Users");
    }
}
