using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Googols.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiTeacherUpgrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FeedbackAtUtc",
                schema: "ai",
                table: "AiMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackComment",
                schema: "ai",
                table: "AiMessages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FeedbackHelpful",
                schema: "ai",
                table: "AiMessages",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageBlobName",
                schema: "ai",
                table: "AiMessages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvalCases",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Question = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExpectedKeywords = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ForbiddenPhrases = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExpectGrounded = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvalCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvalCases_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalSchema: "curriculum",
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvalRuns",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Total = table.Column<int>(type: "int", nullable: false),
                    Passed = table.Column<int>(type: "int", nullable: false),
                    AverageScore = table.Column<double>(type: "float", nullable: false),
                    ResultsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvalRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageLogs",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Feature = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    Characters = table.Column<int>(type: "int", nullable: false),
                    CostUsd = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiMessages_FeedbackHelpful_FeedbackAtUtc",
                schema: "ai",
                table: "AiMessages",
                columns: new[] { "FeedbackHelpful", "FeedbackAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvalCases_SubjectId",
                schema: "ai",
                table: "EvalCases",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalRuns_CreatedAtUtc",
                schema: "ai",
                table: "EvalRuns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsageLogs_CreatedAtUtc",
                schema: "ai",
                table: "UsageLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsageLogs_Feature_CreatedAtUtc",
                schema: "ai",
                table: "UsageLogs",
                columns: new[] { "Feature", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvalCases",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "EvalRuns",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "UsageLogs",
                schema: "ai");

            migrationBuilder.DropIndex(
                name: "IX_AiMessages_FeedbackHelpful_FeedbackAtUtc",
                schema: "ai",
                table: "AiMessages");

            migrationBuilder.DropColumn(
                name: "FeedbackAtUtc",
                schema: "ai",
                table: "AiMessages");

            migrationBuilder.DropColumn(
                name: "FeedbackComment",
                schema: "ai",
                table: "AiMessages");

            migrationBuilder.DropColumn(
                name: "FeedbackHelpful",
                schema: "ai",
                table: "AiMessages");

            migrationBuilder.DropColumn(
                name: "ImageBlobName",
                schema: "ai",
                table: "AiMessages");
        }
    }
}
