using FluentValidation;
using Googols.Application.Curriculum;
using Googols.Domain.Common;
using Googols.Domain.Curricula;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>
/// Catalog management for content managers and admins: see everything (drafts included), add the
/// year → class → subject → chapter levels, and publish or unpublish. Students only ever read Published rows.
/// </summary>
public static class AdminCurriculumEndpoints
{
    public static void MapAdminCurriculumEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/curriculum/admin").WithTags("Curriculum admin").RequireAuthorization(Policies.ContentManager);

        g.MapGet("/tree", async (GoogolsDbContext db, CancellationToken ct) =>
        {
            var boards = await db.CurriculumBoards.AsNoTracking()
                .Include(b => b.Curricula).ThenInclude(c => c.Classes).ThenInclude(k => k.Subjects).ThenInclude(s => s.Chapters)
                .AsSplitQuery()
                .OrderBy(b => b.Name)
                .ToListAsync(ct);

            return boards.Select(b => new AdminBoardDto(b.Id, b.Name, b.Code, b.CountryCode, b.IsEntranceExam,
                b.Curricula.OrderByDescending(c => c.AcademicYear).ThenByDescending(c => c.Version).Select(c => new AdminCurriculumDto(
                    c.Id, c.Name, c.AcademicYear, c.Version, c.Status.ToString(),
                    c.Classes.OrderBy(k => k.GradeNumber).Select(k => new AdminClassDto(k.Id, k.GradeNumber, k.GradeName,
                        k.Subjects.OrderBy(s => s.Sequence).ThenBy(s => s.Name).Select(s => new AdminSubjectDto(
                            s.Id, s.Name, s.Code, s.MediumLanguage, s.Sequence,
                            s.Chapters.OrderBy(ch => ch.Sequence).Select(ch => new AdminChapterDto(ch.Id, ch.Title, ch.Sequence, ch.Status.ToString())).ToList()
                        )).ToList()
                    )).ToList()
                )).ToList()
            )).ToList();
        });

        g.MapPost("/curricula", async (CreateCurriculumRequest req, IValidator<CreateCurriculumRequest> v, GoogolsDbContext db, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            if (!await db.CurriculumBoards.AnyAsync(b => b.Id == req.BoardId, ct))
            {
                return Results.NotFound(new { message = "Board not found." });
            }
            var version = 1 + (await db.Curricula.Where(c => c.BoardId == req.BoardId && c.AcademicYear == req.AcademicYear)
                .MaxAsync(c => (int?)c.Version, ct) ?? 0);
            var curriculum = new Curriculum { BoardId = req.BoardId, Name = req.Name.Trim(), AcademicYear = req.AcademicYear, Version = version };
            db.Curricula.Add(curriculum);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/curriculum/admin/curricula/{curriculum.Id}", new { curriculum.Id, curriculum.Version });
        });

        g.MapPost("/classes", async (CreateClassRequest req, IValidator<CreateClassRequest> v, GoogolsDbContext db, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            if (!await db.Curricula.AnyAsync(c => c.Id == req.CurriculumId, ct))
            {
                return Results.NotFound(new { message = "Academic year not found." });
            }
            if (await db.AcademicClasses.AnyAsync(k => k.CurriculumId == req.CurriculumId && k.GradeNumber == req.GradeNumber, ct))
            {
                return Results.Conflict(new { message = $"Class {req.GradeNumber} already exists in this academic year." });
            }
            var cls = new AcademicClass { CurriculumId = req.CurriculumId, GradeNumber = req.GradeNumber, GradeName = req.GradeName.Trim() };
            db.AcademicClasses.Add(cls);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/curriculum/admin/classes/{cls.Id}", new { cls.Id });
        });

        g.MapPost("/subjects", async (CreateSubjectRequest req, IValidator<CreateSubjectRequest> v, GoogolsDbContext db, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            if (!await db.AcademicClasses.AnyAsync(k => k.Id == req.ClassId, ct))
            {
                return Results.NotFound(new { message = "Class not found." });
            }
            var code = req.Code.Trim().ToUpperInvariant();
            var medium = req.MediumLanguage.ToLowerInvariant();
            if (await db.Subjects.AnyAsync(s => s.ClassId == req.ClassId && s.Code == code && s.MediumLanguage == medium, ct))
            {
                return Results.Conflict(new { message = $"Subject {code} ({medium}) already exists in this class." });
            }
            var subject = new Subject { ClassId = req.ClassId, Name = req.Name.Trim(), Code = code, MediumLanguage = medium, Sequence = req.Sequence };
            db.Subjects.Add(subject);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/curriculum/admin/subjects/{subject.Id}", new { subject.Id });
        });

        g.MapPut("/curricula/{id:guid}/status", async (Guid id, SetStatusRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!PublicationStatusParser.TryParseAdminStatus(req.Status, out var status))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use Draft, Published or Archived."] });
            }
            var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (curriculum is null)
            {
                return Results.NotFound();
            }
            if (status == PublicationStatus.Published && !await db.AcademicClasses.AnyAsync(k => k.CurriculumId == id, ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Add at least one class before publishing."] });
            }
            curriculum.Status = status;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPut("/chapters/{id:guid}/status", async (Guid id, SetStatusRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!PublicationStatusParser.TryParseAdminStatus(req.Status, out var status))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use Draft, Published or Archived."] });
            }
            var chapter = await db.Chapters.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (chapter is null)
            {
                return Results.NotFound();
            }
            chapter.Status = status;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}

/// <summary>Who am I and which roles do I hold, so the React app can route admins to the admin screens.</summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this RouteGroupBuilder v1)
    {
        // Roles come from the database, not the sign-in cookie, so a role granted a moment ago shows up at once.
        v1.MapGet("/account/me", async (HttpContext http, Microsoft.AspNetCore.Identity.UserManager<Infrastructure.Identity.AppUser> users) =>
        {
            var user = await users.GetUserAsync(http.User);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new { email = user.Email, roles = (await users.GetRolesAsync(user)).ToArray() });
        }).WithTags("Account").RequireAuthorization();
    }
}
