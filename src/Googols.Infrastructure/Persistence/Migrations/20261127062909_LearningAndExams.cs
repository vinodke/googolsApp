using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Googols.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LearningAndExams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookmarks",
                schema: "learning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Snippet = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookmarks_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalSchema: "learning",
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChapterMastery",
                schema: "learning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PracticeScore = table.Column<double>(type: "float", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Correct = table.Column<int>(type: "int", nullable: false),
                    Answered = table.Column<int>(type: "int", nullable: false),
                    LastPracticedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChapterMastery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChapterMastery_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalSchema: "curriculum",
                        principalTable: "Chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChapterMastery_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalSchema: "learning",
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Exams",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ChapterIdsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    McqCount = table.Column<int>(type: "int", nullable: false),
                    WrittenCount = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OpensAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosesAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedForStudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassroomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exams_AcademicClasses_ClassId",
                        column: x => x.ClassId,
                        principalSchema: "curriculum",
                        principalTable: "AcademicClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WrittenQuestions",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ModelAnswer = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: true),
                    RubricJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    MaxMarks = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WrittenQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WrittenQuestions_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalSchema: "curriculum",
                        principalTable: "Chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamAttempts",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionIdsJson = table.Column<string>(type: "nvarchar(2500)", maxLength: 2500, nullable: false),
                    WrittenIdsJson = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AnswersJson = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    McqScore = table.Column<int>(type: "int", nullable: false),
                    McqTotal = table.Column<int>(type: "int", nullable: false),
                    WrittenScore = table.Column<double>(type: "float", nullable: true),
                    WrittenMax = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<double>(type: "float", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAttempts_Exams_ExamId",
                        column: x => x.ExamId,
                        principalSchema: "assessment",
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamAttempts_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalSchema: "learning",
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WrittenAnswers",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswerText = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    MaxMarks = table.Column<int>(type: "int", nullable: false),
                    AiScore = table.Column<double>(type: "float", nullable: true),
                    AiFeedbackJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TeacherScore = table.Column<double>(type: "float", nullable: true),
                    TeacherComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MarkedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WrittenAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WrittenAnswers_ExamAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalSchema: "assessment",
                        principalTable: "ExamAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WrittenAnswers_WrittenQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalSchema: "assessment",
                        principalTable: "WrittenQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_StudentId_Kind_RefId",
                schema: "learning",
                table: "Bookmarks",
                columns: new[] { "StudentId", "Kind", "RefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChapterMastery_ChapterId",
                schema: "learning",
                table: "ChapterMastery",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_ChapterMastery_StudentId_ChapterId",
                schema: "learning",
                table: "ChapterMastery",
                columns: new[] { "StudentId", "ChapterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ExamId_SubmittedAtUtc",
                schema: "assessment",
                table: "ExamAttempts",
                columns: new[] { "ExamId", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_StudentId_ExamId",
                schema: "assessment",
                table: "ExamAttempts",
                columns: new[] { "StudentId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_ClassId_Kind_Status",
                schema: "assessment",
                table: "Exams",
                columns: new[] { "ClassId", "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_ClassroomId",
                schema: "assessment",
                table: "Exams",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CreatedForStudentId",
                schema: "assessment",
                table: "Exams",
                column: "CreatedForStudentId");

            migrationBuilder.CreateIndex(
                name: "IX_WrittenAnswers_AttemptId_QuestionId",
                schema: "assessment",
                table: "WrittenAnswers",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WrittenAnswers_QuestionId",
                schema: "assessment",
                table: "WrittenAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_WrittenAnswers_StudentId_Status",
                schema: "assessment",
                table: "WrittenAnswers",
                columns: new[] { "StudentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WrittenQuestions_ChapterId_Status",
                schema: "assessment",
                table: "WrittenQuestions",
                columns: new[] { "ChapterId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookmarks",
                schema: "learning");

            migrationBuilder.DropTable(
                name: "ChapterMastery",
                schema: "learning");

            migrationBuilder.DropTable(
                name: "WrittenAnswers",
                schema: "assessment");

            migrationBuilder.DropTable(
                name: "ExamAttempts",
                schema: "assessment");

            migrationBuilder.DropTable(
                name: "WrittenQuestions",
                schema: "assessment");

            migrationBuilder.DropTable(
                name: "Exams",
                schema: "assessment");
        }
    }
}
