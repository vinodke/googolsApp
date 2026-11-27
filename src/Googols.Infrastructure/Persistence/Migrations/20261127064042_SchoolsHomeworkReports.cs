using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Googols.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SchoolsHomeworkReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WeeklyReportEmail",
                schema: "learning",
                table: "ParentStudentLinks",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                schema: "school",
                table: "Classrooms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SchoolId",
                schema: "school",
                table: "Classrooms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubjectId",
                schema: "school",
                table: "Classrooms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Assignments",
                schema: "school",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChapterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assignments_Classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalSchema: "school",
                        principalTable: "Classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParentReportLogs",
                schema: "learning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentReportLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Schools",
                schema: "school",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssignmentSubmissions",
                schema: "school",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentSubmissions_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "school",
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignmentSubmissions_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalSchema: "learning",
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Divisions",
                schema: "school",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    JoinCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    JoiningOpen = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Divisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Divisions_AcademicClasses_ClassId",
                        column: x => x.ClassId,
                        principalSchema: "curriculum",
                        principalTable: "AcademicClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Divisions_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalSchema: "school",
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SchoolMembers",
                schema: "school",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolMembers_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalSchema: "school",
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SchoolMembers_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DivisionMembers",
                schema: "school",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DivisionMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DivisionMembers_Divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalSchema: "school",
                        principalTable: "Divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DivisionMembers_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalSchema: "learning",
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherAllocations",
                schema: "school",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherAllocations_Divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalSchema: "school",
                        principalTable: "Divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeacherAllocations_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalSchema: "curriculum",
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TimetableSlots",
                schema: "school",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TeacherUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    Label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimetableSlots", x => x.Id);
                    table.CheckConstraint("CK_TimetableSlots_Day", "[DayOfWeek] BETWEEN 1 AND 6 AND [Period] BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_TimetableSlots_Divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalSchema: "school",
                        principalTable: "Divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Classrooms_DivisionId",
                schema: "school",
                table: "Classrooms",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_ClassroomId_DueAtUtc",
                schema: "school",
                table: "Assignments",
                columns: new[] { "ClassroomId", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentSubmissions_AssignmentId_StudentId",
                schema: "school",
                table: "AssignmentSubmissions",
                columns: new[] { "AssignmentId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentSubmissions_StudentId",
                schema: "school",
                table: "AssignmentSubmissions",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_DivisionMembers_DivisionId_StudentId",
                schema: "school",
                table: "DivisionMembers",
                columns: new[] { "DivisionId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DivisionMembers_StudentId",
                schema: "school",
                table: "DivisionMembers",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Divisions_ClassId",
                schema: "school",
                table: "Divisions",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_Divisions_JoinCode",
                schema: "school",
                table: "Divisions",
                column: "JoinCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Divisions_SchoolId",
                schema: "school",
                table: "Divisions",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentReportLogs_ParentUserId_StudentId_WeekStart",
                schema: "learning",
                table: "ParentReportLogs",
                columns: new[] { "ParentUserId", "StudentId", "WeekStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchoolMembers_SchoolId_UserId",
                schema: "school",
                table: "SchoolMembers",
                columns: new[] { "SchoolId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchoolMembers_UserId",
                schema: "school",
                table: "SchoolMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Schools_Code",
                schema: "school",
                table: "Schools",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAllocations_DivisionId_SubjectId",
                schema: "school",
                table: "TeacherAllocations",
                columns: new[] { "DivisionId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAllocations_SubjectId",
                schema: "school",
                table: "TeacherAllocations",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAllocations_TeacherUserId",
                schema: "school",
                table: "TeacherAllocations",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TimetableSlots_DivisionId_DayOfWeek_Period",
                schema: "school",
                table: "TimetableSlots",
                columns: new[] { "DivisionId", "DayOfWeek", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimetableSlots_TeacherUserId",
                schema: "school",
                table: "TimetableSlots",
                column: "TeacherUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentSubmissions",
                schema: "school");

            migrationBuilder.DropTable(
                name: "DivisionMembers",
                schema: "school");

            migrationBuilder.DropTable(
                name: "ParentReportLogs",
                schema: "learning");

            migrationBuilder.DropTable(
                name: "SchoolMembers",
                schema: "school");

            migrationBuilder.DropTable(
                name: "TeacherAllocations",
                schema: "school");

            migrationBuilder.DropTable(
                name: "TimetableSlots",
                schema: "school");

            migrationBuilder.DropTable(
                name: "Assignments",
                schema: "school");

            migrationBuilder.DropTable(
                name: "Divisions",
                schema: "school");

            migrationBuilder.DropTable(
                name: "Schools",
                schema: "school");

            migrationBuilder.DropIndex(
                name: "IX_Classrooms_DivisionId",
                schema: "school",
                table: "Classrooms");

            migrationBuilder.DropColumn(
                name: "WeeklyReportEmail",
                schema: "learning",
                table: "ParentStudentLinks");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                schema: "school",
                table: "Classrooms");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                schema: "school",
                table: "Classrooms");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                schema: "school",
                table: "Classrooms");
        }
    }
}
