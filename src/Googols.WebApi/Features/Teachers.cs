using Googols.Application.Abstractions;
using Googols.Application.Common;
using Googols.Application.Gamification;
using Googols.Domain.Common;
using Googols.Domain.Schools;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>Teacher classrooms: create, share a join code, and follow students' progress.</summary>
public static class TeacherEndpoints
{
    public const int JoinCodeLength = 6;

    public sealed record CreateClassroomRequest(string Name, Guid ClassId, string? SchoolName);
    public sealed record ClassroomDto(Guid Id, string Name, string? SchoolName, Guid ClassId, string ClassName, string BoardCode, string AcademicYear,
        string JoinCode, bool JoiningOpen, int StudentCount);
    public sealed record StudentRowDto(Guid StudentId, string Name, int TotalXp, int Level, int StreakDays, DateOnly? LastActiveDate,
        int LessonsCompleted, int QuizzesTaken, double? QuizAverage);
    public sealed record ChapterInsightDto(Guid ChapterId, string Title, string Subject, int Sequence, int LessonCount, double CompletionRate,
        int QuizAttempts, double? QuizAverage);
    public sealed record ClassroomDetailDto(ClassroomDto Classroom, int TotalLessons, IReadOnlyList<StudentRowDto> Students, IReadOnlyList<ChapterInsightDto> Chapters);
    public sealed record JoinRequest(string Code);
    public sealed record JoiningRequest(bool Open);
    public sealed record MyClassroomDto(Guid Id, string Name, string? SchoolName, string Teacher);

    public static void MapTeacherEndpoints(this RouteGroupBuilder v1)
    {
        var t = v1.MapGroup("/teacher").WithTags("Teachers").RequireAuthorization(Policies.Teacher);

        t.MapGet("/classrooms", async (ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
            await Classrooms(db).Where(c => c.TeacherUserId == me.UserId).OrderBy(c => c.Name).Select(Project(db)).ToListAsync(ct));

        t.MapPost("/classrooms", async (CreateClassroomRequest req, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var name = req.Name?.Trim() ?? "";
            if (name.Length is < 1 or > 100)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Give the classroom a name, e.g. 8B Biology."] });
            }
            if (!await db.AcademicClasses.AnyAsync(k => k.Id == req.ClassId && k.Curriculum!.Status == PublicationStatus.Published, ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["classId"] = ["Choose a published class."] });
            }
            var room = new Classroom
            {
                TeacherUserId = me.UserId!.Value,
                ClassId = req.ClassId,
                Name = name,
                SchoolName = string.IsNullOrWhiteSpace(req.SchoolName) ? null : req.SchoolName.Trim(),
                JoinCode = await DivisionMembership.UniqueCodeAsync(db, ct),
            };
            db.Classrooms.Add(room);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/teacher/classrooms/{room.Id}", new { room.Id, room.JoinCode });
        });

        t.MapPost("/classrooms/{id:guid}/rotate-code", async (Guid id, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var room = await db.Classrooms.FirstOrDefaultAsync(c => c.Id == id && c.TeacherUserId == me.UserId, ct);
            if (room is null)
            {
                return Results.NotFound();
            }
            room.JoinCode = await DivisionMembership.UniqueCodeAsync(db, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { room.JoinCode });
        });

        t.MapPut("/classrooms/{id:guid}/joining", async (Guid id, JoiningRequest req, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.Classrooms.Where(c => c.Id == id && c.TeacherUserId == me.UserId && c.DivisionId == null) // school classes follow their division
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.JoiningOpen, req.Open), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        t.MapDelete("/classrooms/{id:guid}", async (Guid id, ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.Classrooms.Where(c => c.Id == id && c.TeacherUserId == me.UserId)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivedAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        t.MapDelete("/classrooms/{id:guid}/students/{studentId:guid}", async (Guid id, Guid studentId, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Classrooms.AnyAsync(c => c.Id == id && c.TeacherUserId == me.UserId, ct))
            {
                return Results.NotFound();
            }
            await db.ClassroomMembers.Where(m => m.ClassroomId == id && m.StudentId == studentId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        t.MapGet("/classrooms/{id:guid}", async (Guid id, ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var room = await Classrooms(db).Where(c => c.Id == id && c.TeacherUserId == me.UserId).Select(Project(db)).FirstOrDefaultAsync(ct);
            if (room is null)
            {
                return Results.NotFound();
            }
            var today = clock.TodayIn("Asia/Kolkata");
            var memberIds = db.ClassroomMembers.Where(m => m.ClassroomId == id).Select(m => m.StudentId);

            // Lessons and chapters visible to this class.
            var lessons = await db.Lessons.AsNoTracking()
                .Where(l => l.Status == PublicationStatus.Published && l.Chapter!.Status == PublicationStatus.Published && l.Chapter.Subject!.ClassId == room.ClassId)
                .Select(l => new { l.Id, l.ChapterId }).ToListAsync(ct);
            var lessonIds = lessons.Select(l => l.Id).ToList();

            var students = await db.StudentProfiles.AsNoTracking().Where(s => memberIds.Contains(s.Id))
                .Select(s => new
                {
                    s.Id, s.DisplayName, s.TotalXp, s.CurrentStreakDays, s.LastActiveLocalDate,
                    Lessons = db.LessonProgress.Count(p => p.StudentId == s.Id && lessonIds.Contains(p.LessonId)),
                    Quizzes = db.QuizAttempts.Count(a => a.StudentId == s.Id && a.SubmittedAtUtc != null),
                    QuizAvg = db.QuizAttempts.Where(a => a.StudentId == s.Id && a.SubmittedAtUtc != null).Average(a => (double?)a.Percent),
                }).OrderBy(s => s.DisplayName).ToListAsync(ct);

            var chapters = await db.Chapters.AsNoTracking()
                .Where(c => c.Status == PublicationStatus.Published && c.Subject!.ClassId == room.ClassId)
                .OrderBy(c => c.Subject!.Sequence).ThenBy(c => c.Sequence)
                .Select(c => new
                {
                    c.Id, c.Title, c.Sequence, Subject = c.Subject!.Name,
                    Attempts = db.QuizAttempts.Count(a => a.ChapterId == c.Id && a.SubmittedAtUtc != null && memberIds.Contains(a.StudentId)),
                    Avg = db.QuizAttempts.Where(a => a.ChapterId == c.Id && a.SubmittedAtUtc != null && memberIds.Contains(a.StudentId)).Average(a => (double?)a.Percent),
                    Completions = db.LessonProgress.Count(p => p.ChapterId == c.Id && memberIds.Contains(p.StudentId)),
                }).ToListAsync(ct);

            var studentRows = students.Select(s => new StudentRowDto(s.Id, s.DisplayName, s.TotalXp, XpAwardPolicy.LevelFor(s.TotalXp),
                StreakPolicy.Current(s.CurrentStreakDays, s.LastActiveLocalDate, today), s.LastActiveLocalDate, s.Lessons, s.Quizzes,
                s.QuizAvg is { } avg ? Math.Round(avg, 1) : null)).ToList();
            var chapterRows = chapters.Select(c =>
            {
                var lessonCount = lessons.Count(l => l.ChapterId == c.Id);
                var possible = lessonCount * Math.Max(students.Count, 1);
                return new ChapterInsightDto(c.Id, c.Title, c.Subject, c.Sequence, lessonCount,
                    possible == 0 ? 0 : Math.Round(100.0 * c.Completions / possible, 1), c.Attempts, c.Avg is { } a ? Math.Round(a, 1) : null);
            }).ToList();
            return Results.Ok(new ClassroomDetailDto(room, lessonIds.Count, studentRows, chapterRows));
        });

        // ---- Student side
        var s = v1.MapGroup("/students/me/classrooms").WithTags("Teachers").RequireAuthorization(Policies.Student);

        s.MapGet("", async (StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var rows = await (from m in db.ClassroomMembers
                              join c in db.Classrooms on m.ClassroomId equals c.Id
                              join u in db.Users on c.TeacherUserId equals u.Id
                              where m.StudentId == ctx.Student.Id
                              select new { c.Id, c.Name, c.SchoolName, u.FullName, u.Email }).ToListAsync(ct);
            return Results.Ok(rows.Select(r => new MyClassroomDto(r.Id, r.Name, r.SchoolName,
                string.IsNullOrWhiteSpace(r.FullName) ? Privacy.MaskEmail(r.Email) : r.FullName)));
        });

        s.MapPost("/join", async (JoinRequest req, StudentAccess access, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var code = Codes.Normalize(req.Code ?? "");
            var room = await db.Classrooms.FirstOrDefaultAsync(c => c.JoinCode == code && c.JoiningOpen, ct);
            if (room is null && await db.Divisions.FirstOrDefaultAsync(d => d.JoinCode == code && d.JoiningOpen, ct) is { } division)
            {
                // A school division code: join the division and every subject classroom its teachers run.
                if (division.ClassId != ctx.ClassId)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["This division is for a different class than yours."] });
                }
                await DivisionMembership.JoinAsync(db, clock, division, ctx.Student, ct);
                var school = await db.Schools.Where(x => x.Id == division.SchoolId).Select(x => x.Name).FirstAsync(ct);
                return Results.Ok(new { division.Id, Name = $"{division.Name} · {school}" });
            }
            if (room is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["That class code is wrong, or the class isn't taking new students. Check with your teacher."] });
            }
            if (room.ClassId != ctx.ClassId)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["This classroom is for a different class than yours."] });
            }
            if (!await db.ClassroomMembers.AnyAsync(m => m.ClassroomId == room.Id && m.StudentId == ctx.Student.Id, ct))
            {
                db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = room.Id, StudentId = ctx.Student.Id, JoinedAtUtc = clock.UtcNow });
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { room.Id, room.Name });
        }).RequireRateLimiting("auth");

        s.MapDelete("/{id:guid}", async (Guid id, StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            // Leaving a school class leaves the whole division (all its subject classrooms).
            var divisionId = await db.Classrooms.IgnoreQueryFilters().Where(c => c.Id == id).Select(c => c.DivisionId).FirstOrDefaultAsync(ct);
            if (divisionId is { } did)
            {
                await DivisionMembership.LeaveAsync(db, did, ctx.Student.Id, ct);
                return Results.NoContent();
            }
            await db.ClassroomMembers.Where(m => m.ClassroomId == id && m.StudentId == ctx.Student.Id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }

    private static IQueryable<Classroom> Classrooms(GoogolsDbContext db) => db.Classrooms.AsNoTracking();

    private static System.Linq.Expressions.Expression<Func<Classroom, ClassroomDto>> Project(GoogolsDbContext db) => c => new ClassroomDto(
        c.Id, c.Name, c.SchoolName, c.ClassId,
        db.AcademicClasses.Where(k => k.Id == c.ClassId).Select(k => k.GradeName).First(),
        db.AcademicClasses.Where(k => k.Id == c.ClassId).Select(k => k.Curriculum!.Board!.Code).First(),
        db.AcademicClasses.Where(k => k.Id == c.ClassId).Select(k => k.Curriculum!.AcademicYear).First(),
        c.JoinCode, c.JoiningOpen,
        db.ClassroomMembers.Count(m => m.ClassroomId == c.Id));

}
