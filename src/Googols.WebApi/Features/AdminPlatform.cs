using Googols.Application.Abstractions;
using Googols.Application.Curriculum;
using Googols.Domain.Billing;
using Googols.Domain.Common;
using Googols.Domain.Curricula;
using Googols.Infrastructure.Identity;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>Admin console: platform metrics (Phase 12), user and role management, curriculum edits and CSV import.</summary>
public static class AdminPlatformEndpoints
{
    public sealed record DayPoint(DateOnly Day, int Value);
    public sealed record MetricsDto(int Users, int Students, int Parents, int Teachers, int ActiveStudents7d, int PublishedLessons, int PublishedQuestions,
        int QuizAttempts7d, int AiMessages7d, long AiTokens7d, int ActiveSubscriptions, long RevenuePaise30d,
        IReadOnlyList<DayPoint> AiMessagesDaily, IReadOnlyList<DayPoint> LessonsCompletedDaily, IReadOnlyList<DayPoint> NewStudentsDaily);
    public sealed record UserDto(Guid Id, string? Email, DateTime CreatedAtUtc, IReadOnlyList<string> Roles, bool Locked, bool HasStudentProfile);
    public sealed record UsersPage(int Total, IReadOnlyList<UserDto> Users);
    public sealed record RoleChange(string Role, bool Grant);
    public sealed record LockRequest(bool Locked);
    public sealed record RenameRequest(string Name, int? Sequence);
    public sealed record CsvImportResult(bool Applied, int Rows, int ClassesCreated, int SubjectsCreated, int ChaptersCreated, int ChaptersUpdated, IReadOnlyList<string> Errors);

    public static void MapAdminPlatformEndpoints(this RouteGroupBuilder v1)
    {
        var admin = v1.MapGroup("/admin").WithTags("Admin").RequireAuthorization(Policies.Admin);

        admin.MapGet("/metrics", async (GoogolsDbContext db, IClock clock, CancellationToken ct) =>
        {
            var now = clock.UtcNow;
            var week = now.AddDays(-7);
            var from14 = now.Date.AddDays(-13);
            var today = clock.TodayIn("Asia/Kolkata");

            async Task<int> InRole(string role) => await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where r.Name == role select ur.UserId).Distinct().CountAsync(ct);

            var aiDaily = await db.AiMessages.Where(m => m.Role == "user" && m.CreatedAtUtc >= from14)
                .GroupBy(m => m.CreatedAtUtc.Date).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            var lessonDaily = await db.LessonProgress.Where(p => p.CompletedAtUtc >= from14)
                .GroupBy(p => p.CompletedAtUtc.Date).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            var studentDaily = await db.StudentProfiles.Where(s => s.CreatedAtUtc >= from14)
                .GroupBy(s => s.CreatedAtUtc.Date).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            IReadOnlyList<DayPoint> Series(IEnumerable<(DateTime Day, int N)> data)
            {
                var map = data.ToDictionary(x => x.Day, x => x.N);
                return Enumerable.Range(0, 14).Select(i => from14.AddDays(i)).Select(d => new DayPoint(DateOnly.FromDateTime(d), map.GetValueOrDefault(d))).ToList();
            }

            return new MetricsDto(
                await db.Users.CountAsync(ct),
                await db.StudentProfiles.CountAsync(ct),
                await InRole(Roles.Parent),
                await InRole(Roles.Teacher),
                await db.StudentProfiles.CountAsync(s => s.LastActiveLocalDate >= today.AddDays(-7), ct),
                await db.Lessons.CountAsync(l => l.Status == PublicationStatus.Published, ct),
                await db.Questions.CountAsync(q => q.Status == PublicationStatus.Published, ct),
                await db.QuizAttempts.CountAsync(a => a.SubmittedAtUtc >= week, ct),
                await db.AiMessages.CountAsync(m => m.Role == "user" && m.CreatedAtUtc >= week, ct),
                await db.AiMessages.Where(m => m.CreatedAtUtc >= week).SumAsync(m => (long)(m.InputTokens + m.OutputTokens), ct),
                await db.Subscriptions.CountAsync(s => s.StartsAtUtc <= now && s.EndsAtUtc > now, ct),
                await db.Payments.Where(p => p.Status == PaymentStatus.Paid && p.PaidAtUtc >= now.AddDays(-30)).SumAsync(p => (long)p.AmountPaise, ct),
                Series(aiDaily.Select(x => (x.Key, x.N))),
                Series(lessonDaily.Select(x => (x.Key, x.N))),
                Series(studentDaily.Select(x => (x.Key, x.N))));
        });

        admin.MapGet("/users", async (string? search, string? role, int? page, GoogolsDbContext db, CancellationToken ct) =>
        {
            var q = db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(u => u.Email!.Contains(s));
            }
            if (!string.IsNullOrWhiteSpace(role))
            {
                var ids = from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where r.Name == role select ur.UserId;
                q = q.Where(u => ids.Contains(u.Id));
            }
            var total = await q.CountAsync(ct);
            var p = Math.Max(page ?? 1, 1);
            var users = await q.OrderByDescending(u => u.CreatedAtUtc).ThenBy(u => u.Email).Skip((p - 1) * 25).Take(25)
                .Select(u => new { u.Id, u.Email, u.CreatedAtUtc, u.LockoutEnd }).ToListAsync(ct);
            var userIds = users.Select(u => u.Id).ToList();
            var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where userIds.Contains(ur.UserId) select new { ur.UserId, r.Name }).ToListAsync(ct);
            var students = (await db.StudentProfiles.Where(s => userIds.Contains(s.UserId)).Select(s => s.UserId).ToListAsync(ct)).ToHashSet();
            return new UsersPage(total, users.Select(u => new UserDto(u.Id, u.Email, u.CreatedAtUtc,
                roles.Where(r => r.UserId == u.Id).Select(r => r.Name!).OrderBy(x => x).ToList(),
                u.LockoutEnd > DateTimeOffset.UtcNow, students.Contains(u.Id))).ToList());
        });

        admin.MapPut("/users/{id:guid}/roles", async (Guid id, RoleChange req, ICurrentUser me, UserManager<AppUser> users, CancellationToken ct) =>
        {
            if (!Roles.All.Contains(req.Role))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = [$"Unknown role. Use one of: {string.Join(", ", Roles.All)}."] });
            }
            // Only a SuperAdmin can hand out or remove admin powers, and nobody can demote themselves by accident.
            if (req.Role is Roles.SuperAdmin or Roles.Admin && !me.IsInRole(Roles.SuperAdmin))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only a super admin can change admin roles.");
            }
            if (id == me.UserId && !req.Grant && req.Role is Roles.SuperAdmin or Roles.Admin)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You can't remove your own admin role.");
            }
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null)
            {
                return Results.NotFound();
            }
            var has = await users.IsInRoleAsync(user, req.Role);
            var result = req.Grant && !has ? await users.AddToRoleAsync(user, req.Role)
                : !req.Grant && has ? await users.RemoveFromRoleAsync(user, req.Role)
                : IdentityResult.Success;
            if (result.Succeeded)
            {
                await users.UpdateSecurityStampAsync(user); // their sign-in refreshes with the new roles
            }
            return result.Succeeded ? Results.NoContent() : Results.Problem(title: string.Join(" ", result.Errors.Select(e => e.Description)));
        });

        admin.MapPut("/users/{id:guid}/lock", async (Guid id, LockRequest req, ICurrentUser me, UserManager<AppUser> users, CancellationToken ct) =>
        {
            if (id == me.UserId)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You can't lock your own account.");
            }
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null)
            {
                return Results.NotFound();
            }
            if (await users.IsInRoleAsync(user, Roles.SuperAdmin) && !me.IsInRole(Roles.SuperAdmin))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only a super admin can lock another super admin.");
            }
            await users.SetLockoutEnabledAsync(user, true);
            await users.SetLockoutEndDateAsync(user, req.Locked ? DateTimeOffset.MaxValue : null);
            await users.UpdateSecurityStampAsync(user); // ends their current sessions
            return Results.NoContent();
        });

        // ---- Curriculum edits (content managers)
        var cm = v1.MapGroup("/curriculum/admin").WithTags("Curriculum admin").RequireAuthorization(Policies.ContentManager);

        cm.MapPut("/boards/{id:guid}", async (Guid id, RenameRequest req, GoogolsDbContext db, CancellationToken ct) =>
            await Rename(db.CurriculumBoards.Where(x => x.Id == id), req, 200, (u, name) => u.SetProperty(x => x.Name, name), ct));
        cm.MapPut("/curricula/{id:guid}", async (Guid id, RenameRequest req, GoogolsDbContext db, CancellationToken ct) =>
            await Rename(db.Curricula.Where(x => x.Id == id), req, 200, (u, name) => u.SetProperty(x => x.Name, name), ct));
        cm.MapPut("/classes/{id:guid}", async (Guid id, RenameRequest req, GoogolsDbContext db, CancellationToken ct) =>
            await Rename(db.AcademicClasses.Where(x => x.Id == id), req, 50, (u, name) => u.SetProperty(x => x.GradeName, name), ct));
        cm.MapPut("/subjects/{id:guid}", async (Guid id, RenameRequest req, GoogolsDbContext db, CancellationToken ct) =>
            await Rename(db.Subjects.Where(x => x.Id == id), req, 200,
                (u, name) => req.Sequence is { } seq ? u.SetProperty(x => x.Name, name).SetProperty(x => x.Sequence, seq) : u.SetProperty(x => x.Name, name), ct));
        cm.MapPut("/chapters/{id:guid}", async (Guid id, RenameRequest req, GoogolsDbContext db, CancellationToken ct) =>
            await Rename(db.Chapters.Where(x => x.Id == id), req, 300,
                (u, name) => req.Sequence is { } seq ? u.SetProperty(x => x.Title, name).SetProperty(x => x.Sequence, seq) : u.SetProperty(x => x.Title, name), ct));

        // Deletes are only allowed while nothing depends on the item; otherwise unpublish it instead.
        cm.MapDelete("/boards/{id:guid}", async (Guid id, GoogolsDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (await db.Curricula.AnyAsync(c => c.BoardId == id, ct))
            {
                return InUse("Delete its academic years first.");
            }
            var n = await db.CurriculumBoards.Where(b => b.Id == id).ExecuteUpdateAsync(u => u.SetProperty(b => b.ArchivedAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
        cm.MapDelete("/curricula/{id:guid}", async (Guid id, GoogolsDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (await db.Enrollments.AnyAsync(e => e.CurriculumId == id, ct))
            {
                return InUse("Students are enrolled in this academic year. Unpublish it instead.");
            }
            if (await db.AcademicClasses.AnyAsync(k => k.CurriculumId == id, ct))
            {
                return InUse("Delete its classes first.");
            }
            var n = await db.Curricula.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivedAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
        cm.MapDelete("/classes/{id:guid}", async (Guid id, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await db.Enrollments.AnyAsync(e => e.ClassId == id, ct) || await db.Classrooms.IgnoreQueryFilters().AnyAsync(c => c.ClassId == id, ct))
            {
                return InUse("Students or classrooms use this class.");
            }
            if (await db.Subjects.AnyAsync(s => s.ClassId == id, ct))
            {
                return InUse("Delete its subjects first.");
            }
            var n = await db.AcademicClasses.Where(k => k.Id == id).ExecuteDeleteAsync(ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
        cm.MapDelete("/subjects/{id:guid}", async (Guid id, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await db.Chapters.AnyAsync(c => c.SubjectId == id, ct))
            {
                return InUse("Delete its chapters first.");
            }
            if (await db.AiConversations.IgnoreQueryFilters().AnyAsync(c => c.SubjectId == id, ct))
            {
                return InUse("Students have AI chats in this subject.");
            }
            var n = await db.Subjects.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
        cm.MapDelete("/chapters/{id:guid}", async (Guid id, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await db.Lessons.IgnoreQueryFilters().AnyAsync(l => l.ChapterId == id, ct)
                || await db.Questions.IgnoreQueryFilters().AnyAsync(q => q.ChapterId == id, ct)
                || await db.Topics.AnyAsync(t => t.ChapterId == id, ct))
            {
                return InUse("This chapter has lessons or questions. Unpublish it instead.");
            }
            var n = await db.Chapters.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // CSV import: ?apply=false checks the file and reports what would change; ?apply=true writes it.
        cm.MapPost("/curricula/{id:guid}/import", async (Guid id, bool? apply, HttpRequest http, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Curricula.AnyAsync(c => c.Id == id, ct))
            {
                return Results.NotFound();
            }
            using var reader = new StreamReader(http.Body);
            var text = await reader.ReadToEndAsync(ct);
            if (text.Length > 2_000_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["The CSV must be under 2 MB."] });
            }
            var parsed = CurriculumCsv.Parse(text);
            if (parsed.Errors.Count > 0 || apply != true)
            {
                var preview = await PlanImport(db, id, parsed.Rows, write: false, ct);
                return Results.Ok(preview with { Errors = parsed.Errors, Applied = false });
            }
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await PlanImport(db, id, parsed.Rows, write: true, ct);
            await tx.CommitAsync(ct);
            return Results.Ok(result with { Applied = true });
        }).DisableAntiforgery();
    }

    private static IResult InUse(string why) => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: $"Can't delete: {why}");

    private static async Task<IResult> Rename<T>(IQueryable<T> q, RenameRequest req, int max,
        Func<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<T>, string, Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<T>> set, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > max)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"Enter a name of 1–{max} characters."] });
        }
        if (req.Sequence is < 1 or > 999)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["sequence"] = ["Order must be 1–999."] });
        }
        var n = await q.ExecuteUpdateAsync(u => set(u, name), ct);
        return n == 0 ? Results.NotFound() : Results.NoContent();
    }

    /// <summary>Matches rows to existing classes / subjects / chapters; creates what's missing when <paramref name="write"/>.</summary>
    private static async Task<CsvImportResult> PlanImport(GoogolsDbContext db, Guid curriculumId, IReadOnlyList<CurriculumCsv.Row> rows, bool write, CancellationToken ct)
    {
        var classes = await db.AcademicClasses.Where(k => k.CurriculumId == curriculumId).ToListAsync(ct);
        var classIds = classes.Select(k => k.Id).ToList();
        var subjects = await db.Subjects.Where(s => classIds.Contains(s.ClassId)).ToListAsync(ct);
        var subjectIds = subjects.Select(s => s.Id).ToList();
        var chapters = await db.Chapters.Where(c => subjectIds.Contains(c.SubjectId)).ToListAsync(ct);
        int newClasses = 0, newSubjects = 0, newChapters = 0, updated = 0;

        foreach (var row in rows)
        {
            var cls = classes.FirstOrDefault(k => k.GradeNumber == row.Grade);
            if (cls is null)
            {
                cls = new AcademicClass { CurriculumId = curriculumId, GradeNumber = row.Grade, GradeName = row.Grade == 0 ? "Entrance" : $"Class {row.Grade}" };
                classes.Add(cls);
                newClasses++;
                if (write) db.AcademicClasses.Add(cls);
            }
            var subject = subjects.FirstOrDefault(s => s.ClassId == cls.Id && s.Code == row.SubjectCode && s.MediumLanguage == row.Medium);
            if (subject is null)
            {
                subject = new Subject { ClassId = cls.Id, Code = row.SubjectCode, Name = row.SubjectName, MediumLanguage = row.Medium, Sequence = subjects.Count(s => s.ClassId == cls.Id) + 1 };
                subjects.Add(subject);
                newSubjects++;
                if (write) db.Subjects.Add(subject);
            }
            var chapter = chapters.FirstOrDefault(c => c.SubjectId == subject.Id && c.Sequence == row.ChapterNo);
            if (chapter is null)
            {
                chapter = new Chapter { SubjectId = subject.Id, Sequence = row.ChapterNo, Title = row.ChapterTitle };
                chapters.Add(chapter);
                newChapters++;
                if (write) db.Chapters.Add(chapter);
            }
            else if (chapter.Title != row.ChapterTitle)
            {
                updated++;
                if (write) chapter.Title = row.ChapterTitle; // fixes typos; status is left as it was
            }
        }
        if (write)
        {
            await db.SaveChangesAsync(ct);
        }
        return new CsvImportResult(write, rows.Count, newClasses, newSubjects, newChapters, updated, []);
    }
}
