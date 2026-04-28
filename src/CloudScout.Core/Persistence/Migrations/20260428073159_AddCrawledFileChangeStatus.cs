using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudScout.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrawledFileChangeStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeStatus",
                table: "CrawledFiles",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeStatus",
                table: "CrawledFiles");
        }
    }
}
