using FluentValidation;
using Googols.Application.Curriculum;
using Googols.Domain.Common;
using Googols.Domain.Curricula;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

public static class CurriculumEndpoints
{
    public static void MapCurriculumEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/curriculum").WithTags("Curriculum").RequireAuthorization();

        // Reads: any signed-in user. Students only ever see Published curricula and chapters.
        g.MapGet("/boards", async (GoogolsDbContext db, CancellationToken ct) =>
            await db.CurriculumBoards.AsNoTracking().OrderBy(b => b.Name)
                .Select(b => new BoardDto(b.Id, b.Name, b.Code, b.CountryCode, b.IsEntranceExam)).ToListAsync(ct));

        g.MapGet("/boards/{boardId:guid}/curricula", async (Guid boardId, GoogolsDbContext db, CancellationToken ct) =>
            await db.Curricula.AsNoTracking()
                .Where(c => c.BoardId == boardId && c.Status == PublicationStatus.Published)
                .OrderByDescending(c => c.AcademicYear)
                .Select(c => new { c.Id, c.Name, c.AcademicYear, c.Version }).ToListAsync(ct));

        g.MapGet("/curricula/{curriculumId:guid}/classes", async (Guid curriculumId, GoogolsDbContext db, CancellationToken ct) =>
            await db.AcademicClasses.AsNoTracking().Where(c => c.CurriculumId == curriculumId).OrderBy(c => c.GradeNumber)
                .Select(c => new ClassDto(c.Id, c.GradeNumber, c.GradeName)).ToListAsync(ct));

        g.MapGet("/classes/{classId:guid}/subjects", async (Guid classId, GoogolsDbContext db, CancellationToken ct) =>
            await db.Subjects.AsNoTracking().Where(s => s.ClassId == classId).OrderBy(s => s.Sequence)
                .Select(s => new SubjectDto(s.Id, s.Name, s.Code, s.MediumLanguage)).ToListAsync(ct));

        g.MapGet("/subjects/{subjectId:guid}/chapters", async (Guid subjectId, GoogolsDbContext db, CancellationToken ct) =>
            await db.Chapters.AsNoTracking().Where(c => c.SubjectId == subjectId && c.Status == PublicationStatus.Published)
                .OrderBy(c => c.Sequence).Select(c => new ChapterDto(c.Id, c.Title, c.Sequence)).ToListAsync(ct));

        g.MapGet("/chapters/{chapterId:guid}/topics", async (Guid chapterId, GoogolsDbContext db, CancellationToken ct) =>
            await db.Topics.AsNoTracking().Where(t => t.ChapterId == chapterId).OrderBy(t => t.Sequence)
                .Select(t => new TopicDto(t.Id, t.Title, t.Sequence)).ToListAsync(ct));

        // Writes: content managers only. New items start as Draft; publishing is a separate reviewed step.
        var admin = g.MapGroup("").RequireAuthorization(Policies.ContentManager);

        admin.MapPost("/boards", async (CreateBoardRequest req, IValidator<CreateBoardRequest> v, GoogolsDbContext db, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            if (await db.CurriculumBoards.AnyAsync(b => b.Code == req.Code, ct))
            {
                return Results.Conflict(new { message = $"A board with code {req.Code} already exists." });
            }
            var board = new CurriculumBoard { Name = req.Name.Trim(), Code = req.Code, CountryCode = req.CountryCode.ToUpperInvariant(), IsEntranceExam = req.IsEntranceExam };
            db.CurriculumBoards.Add(board);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/curriculum/boards/{board.Id}", new BoardDto(board.Id, board.Name, board.Code, board.CountryCode, board.IsEntranceExam));
        });

        admin.MapPost("/chapters", async (CreateChapterRequest req, IValidator<CreateChapterRequest> v, GoogolsDbContext db, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            if (!await db.Subjects.AnyAsync(s => s.Id == req.SubjectId, ct))
            {
                return Results.NotFound();
            }
            var chapter = new Chapter { SubjectId = req.SubjectId, Title = req.Title.Trim(), Sequence = req.Sequence };
            db.Chapters.Add(chapter);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/curriculum/chapters/{chapter.Id}", new ChapterDto(chapter.Id, chapter.Title, chapter.Sequence));
        });
    }
}
