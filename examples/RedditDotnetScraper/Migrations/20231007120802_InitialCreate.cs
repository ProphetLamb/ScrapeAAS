using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RedditDotnetScraper.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
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
                    table.PrimaryKey("PK_Comments", x => x.CommentUrl);
                    table.ForeignKey(
                        name: "FK_Comments_Users_PostedById",
                        column: x => x.PostedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
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
                    table.PrimaryKey("PK_Posts", x => x.PostUrl);
                    table.ForeignKey(
                        name: "FK_Posts_Users_PostedById",
                        column: x => x.PostedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PostedById",
                table: "Comments",
                column: "PostedById");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_PostedById",
                table: "Posts",
                column: "PostedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "Posts");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
