using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudScout.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AccountIdentifier = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EncryptedRefreshToken = table.Column<byte[]>(type: "BLOB", nullable: false),
                    TokenExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConnectedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxonomyName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalFilesFound = table.Column<int>(type: "INTEGER", nullable: false),
                    ClassifiedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastProcessedExternalPath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanSessions_CloudConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "CloudConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrawledFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalFileId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ExternalPath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ParentFolderPath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DiscoveredUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawledFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrawledFiles_ScanSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ScanSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FileSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SuggestedCategoryId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ConfidenceScore = table.Column<double>(type: "REAL", nullable: false),
                    ClassificationTier = table.Column<int>(type: "INTEGER", nullable: false),
                    ClassificationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileSuggestions_CrawledFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "CrawledFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudConnections_Provider_AccountIdentifier",
                table: "CloudConnections",
                columns: new[] { "Provider", "AccountIdentifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CloudConnections_Status",
                table: "CloudConnections",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledFiles_SessionId",
                table: "CrawledFiles",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledFiles_SessionId_ExternalFileId",
                table: "CrawledFiles",
                columns: new[] { "SessionId", "ExternalFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileSuggestions_FileId",
                table: "FileSuggestions",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_FileSuggestions_FileId_ClassificationTier",
                table: "FileSuggestions",
                columns: new[] { "FileId", "ClassificationTier" });

            migrationBuilder.CreateIndex(
                name: "IX_FileSuggestions_UserStatus",
                table: "FileSuggestions",
                column: "UserStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ScanSessions_ConnectionId",
                table: "ScanSessions",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanSessions_StartedUtc",
                table: "ScanSessions",
                column: "StartedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ScanSessions_Status",
                table: "ScanSessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileSuggestions");

            migrationBuilder.DropTable(
                name: "CrawledFiles");

            migrationBuilder.DropTable(
                name: "ScanSessions");

            migrationBuilder.DropTable(
                name: "CloudConnections");
        }
    }
}
