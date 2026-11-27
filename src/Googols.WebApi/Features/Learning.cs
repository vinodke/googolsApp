using FluentValidation;
using Googols.Application.Abstractions;
using Googols.Application.Content;
using Googols.Application.Gamification;
using Googols.Domain.Common;
using Googols.Domain.Content;
using Googols.Domain.Students;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>Resolves the signed-in student and the class they are enrolled in. Every student route goes through this.</summary>
public sealed class StudentAccess(GoogolsDbContext db, ICurrentUser me)
{
    public sealed record Context(StudentProfile Student, Guid ClassId);

    public async Task<Context?> GetAsync(CancellationToken ct)
    {
        var student = await db.StudentProfiles.FirstOrDefaultAsync(s => s.UserId == me.UserId, ct);
        if (student is null)
        {
            return null;
        }
        var classId = await db.Enrollments.Where(e => e.StudentId == student.Id && e.IsActive).Select(e => (Guid?)e.ClassId).FirstOrDefaultAsync(ct);
        return classId is null ? null : new Context(student, classId.Value);
    }

    /// <summary>Published lessons in published chapters of the student's class: the only lessons a student may open.</summary>
    public IQueryable<Lesson> VisibleLessons(Guid classId) =>
        db.Lessons.Where(l => l.Status == PublicationStatus.Published
            && l.Chapter!.Status == PublicationStatus.Published
            && l.Chapter.Subject!.ClassId == classId);
}

/// <summary>Rebuilds the AI retrieval chunks of a lesson. Chunks exist only while the lesson is Published.</summary>
public sealed class LessonIndexer(GoogolsDbContext db)
{
    public async Task ReindexAsync(Lesson lesson, CancellationToken ct)
    {
        await db.ContentChunks.Where(c => c.LessonId == lesson.Id).ExecuteDeleteAsync(ct);
        if (lesson.Status != PublicationStatus.Published || lesson.ArchivedAtUtc is not null)
        {
            return;
        }
        var subjectId = await db.Chapters.Where(c => c.Id == lesson.ChapterId).Select(c => c.SubjectId).FirstAsync(ct);
        var pieces = TextChunker.Chunk(lesson.Body);
        for (var i = 0; i < pieces.Count; i++)
        {
            db.ContentChunks.Add(new ContentChunk
            {
                LessonId = lesson.Id,
                ChapterId = lesson.ChapterId,
                SubjectId = subjectId,
                Sequence = lesson.Sequence * 1000 + i,
                Title = lesson.Title,
                Text = pieces[i],
                Kind = lesson.Kind,
            });
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Records real learning activity: updates the daily streak and pays the once-a-day streak XP.</summary>
public sealed class LearningActivityService(GoogolsDbContext db, IClock clock, XpLedgerService xp)
{
    public async Task<int> RecordAsync(StudentProfile student, CancellationToken ct)
    {
        var today = clock.TodayIn("Asia/Kolkata");
        var next = StreakPolicy.Next(student.CurrentStreakDays, student.LastActiveLocalDate, today);
        if (next.IsNewDay)
        {
            await db.StudentProfiles.Where(s => s.Id == student.Id).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.CurrentStreakDays, next.StreakDays)
                .SetProperty(s => s.LastActiveLocalDate, today), ct);
            await xp.AwardAsync(new XpAwardPolicy.LearningEvent("daily-active", student.Id, student.Id, Day: today), ct);
        }
        return next.StreakDays;
    }
}

public static partial class LearningEndpoints
{
    private const string AttachmentContainer = "lesson-files";

    /// <summary>Allowed lesson picture names: pic-0 … pic-99 and poster (nothing that could reach another blob).</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^(pic-[0-9]{1,2}|poster)$")]
    private static partial System.Text.RegularExpressions.Regex LessonPictureName();

    public static string LessonPictureBlob(Guid lessonId, string name) => $"lessons/{lessonId:D}/{name}.jpg";

    public static void MapLearningEndpoints(this RouteGroupBuilder v1)
    {
        MapStudent(v1.MapGroup("/learning").WithTags("Learning").RequireAuthorization(Policies.Student));
        MapAdmin(v1.MapGroup("/curriculum/admin").WithTags("Content admin").RequireAuthorization(Policies.ContentManager));
    }

    private static void MapStudent(RouteGroupBuilder g)
    {
        g.MapGet("/summary", async (StudentAccess access, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var subjects = await db.Subjects.AsNoTracking().Where(s => s.ClassId == ctx.ClassId).OrderBy(s => s.Sequence)
                .Select(s => new { s.Id, s.Name, s.Code }).ToListAsync(ct);
            var totals = await access.VisibleLessons(ctx.ClassId).GroupBy(l => l.Chapter!.SubjectId)
                .Select(g => new { SubjectId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.SubjectId, x => x.Count, ct);
            var done = await (from p in db.LessonProgress
                              join l in access.VisibleLessons(ctx.ClassId) on p.LessonId equals l.Id
                              where p.StudentId == ctx.Student.Id
                              group p by p.SubjectId into g
                              select new { SubjectId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.SubjectId, x => x.Count, ct);
            var list = subjects.Select(s => new SubjectProgressDto(s.Id, s.Name, s.Code, totals.GetValueOrDefault(s.Id), done.GetValueOrDefault(s.Id))).ToList();
            var streak = StreakPolicy.Current(ctx.Student.CurrentStreakDays, ctx.Student.LastActiveLocalDate, clock.TodayIn("Asia/Kolkata"));
            return Results.Ok(new LearningSummaryDto(list.Sum(s => s.TotalLessons), list.Sum(s => s.CompletedLessons), streak, list));
        });

        g.MapGet("/subjects/{subjectId:guid}/chapters", async (Guid subjectId, StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx || !await db.Subjects.AnyAsync(s => s.Id == subjectId && s.ClassId == ctx.ClassId, ct))
            {
                return Results.NotFound();
            }
            var chapters = await db.Chapters.AsNoTracking().Where(c => c.SubjectId == subjectId && c.Status == PublicationStatus.Published)
                .OrderBy(c => c.Sequence).Select(c => new { c.Id, c.Title, c.Sequence }).ToListAsync(ct);
            var lessons = await access.VisibleLessons(ctx.ClassId).Where(l => l.Chapter!.SubjectId == subjectId)
                .Select(l => new { l.Id, l.ChapterId }).ToListAsync(ct);
            var completed = (await db.LessonProgress.Where(p => p.StudentId == ctx.Student.Id && p.SubjectId == subjectId)
                .Select(p => p.LessonId).ToListAsync(ct)).ToHashSet();
            return Results.Ok(chapters.Select(c => new ChapterProgressDto(c.Id, c.Title, c.Sequence,
                lessons.Count(l => l.ChapterId == c.Id), lessons.Count(l => l.ChapterId == c.Id && completed.Contains(l.Id)))));
        });

        g.MapGet("/chapters/{chapterId:guid}", async (Guid chapterId, StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var chapter = await db.Chapters.AsNoTracking()
                .Where(c => c.Id == chapterId && c.Status == PublicationStatus.Published && c.Subject!.ClassId == ctx.ClassId)
                .Select(c => new { c.Id, c.Title, c.Sequence, c.SubjectId, SubjectName = c.Subject!.Name }).FirstOrDefaultAsync(ct);
            if (chapter is null)
            {
                return Results.NotFound();
            }
            var completed = (await db.LessonProgress.Where(p => p.StudentId == ctx.Student.Id && p.ChapterId == chapterId)
                .Select(p => p.LessonId).ToListAsync(ct)).ToHashSet();
            var lessons = await access.VisibleLessons(ctx.ClassId).Where(l => l.ChapterId == chapterId).OrderBy(l => l.Sequence)
                .Select(l => new { l.Id, l.Title, l.Sequence, l.EstimatedMinutes, HasAttachment = l.AttachmentBlobName != null }).ToListAsync(ct);
            return Results.Ok(new StudentChapterDto(chapter.Id, chapter.Title, chapter.Sequence, chapter.SubjectId, chapter.SubjectName,
                lessons.Select(l => new StudentLessonItemDto(l.Id, l.Title, l.Sequence, l.EstimatedMinutes, completed.Contains(l.Id), l.HasAttachment)).ToList()));
        });

        g.MapGet("/lessons/{lessonId:guid}", async (Guid lessonId, StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var l = await access.VisibleLessons(ctx.ClassId).AsNoTracking().Where(x => x.Id == lessonId)
                .Select(x => new { x.Id, x.Title, x.Body, x.Language, x.Kind, x.EstimatedMinutes, x.ChapterId, ChapterTitle = x.Chapter!.Title,
                    x.Chapter.SubjectId, SubjectName = x.Chapter.Subject!.Name, x.AttachmentFileName, x.AttachmentBytes, x.Sequence })
                .FirstOrDefaultAsync(ct);
            if (l is null)
            {
                return Results.NotFound();
            }
            var siblings = await access.VisibleLessons(ctx.ClassId).Where(x => x.ChapterId == l.ChapterId).OrderBy(x => x.Sequence)
                .Select(x => x.Id).ToListAsync(ct);
            var i = siblings.IndexOf(l.Id);
            var completed = await db.LessonProgress.AnyAsync(p => p.StudentId == ctx.Student.Id && p.LessonId == l.Id, ct);
            return Results.Ok(new StudentLessonDto(l.Id, l.Title, l.Body, l.Language, l.Kind.ToString(), l.EstimatedMinutes, l.ChapterId, l.ChapterTitle,
                l.SubjectId, l.SubjectName, completed, l.AttachmentFileName, l.AttachmentBytes,
                i > 0 ? siblings[i - 1] : null, i >= 0 && i < siblings.Count - 1 ? siblings[i + 1] : null));
        });

        g.MapGet("/lessons/{lessonId:guid}/attachment", async (Guid lessonId, StudentAccess access, IFileStorage files, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var l = await access.VisibleLessons(ctx.ClassId).AsNoTracking().Where(x => x.Id == lessonId && x.AttachmentBlobName != null)
                .Select(x => new { x.AttachmentBlobName, x.AttachmentFileName, x.AttachmentContentType }).FirstOrDefaultAsync(ct);
            if (l is null || await files.OpenReadAsync(AttachmentContainer, l.AttachmentBlobName!, ct) is not { } stream)
            {
                return Results.NotFound();
            }
            return Results.File(stream, l.AttachmentContentType ?? "application/octet-stream", l.AttachmentFileName);
        });

        // Pictures placed inside a lesson's text with "![caption](pic:N)" / "![caption](pic:poster)".
        // Stored in Blob as lesson-files/lessons/{lessonId}/pic-N.jpg and .../poster.jpg; same access rule as the lesson.
        g.MapGet("/lessons/{lessonId:guid}/pictures/{name}", async (Guid lessonId, string name, StudentAccess access, IFileStorage files,
            HttpResponse response, CancellationToken ct) =>
        {
            if (!LessonPictureName().IsMatch(name) || await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            if (!await access.VisibleLessons(ctx.ClassId).AnyAsync(x => x.Id == lessonId, ct)
                || await files.OpenReadAsync(AttachmentContainer, LessonPictureBlob(lessonId, name), ct) is not { } stream)
            {
                return Results.NotFound();
            }
            response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(stream, "image/jpeg");
        });

        g.MapPost("/lessons/{lessonId:guid}/complete", async (Guid lessonId, StudentAccess access, GoogolsDbContext db, IClock clock,
            XpLedgerService xp, LearningActivityService activity, BadgeService badges, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var l = await access.VisibleLessons(ctx.ClassId).AsNoTracking().Where(x => x.Id == lessonId)
                .Select(x => new { x.Id, x.ChapterId, x.Chapter!.SubjectId }).FirstOrDefaultAsync(ct);
            if (l is null)
            {
                return Results.NotFound();
            }
            if (!await db.LessonProgress.AnyAsync(p => p.StudentId == ctx.Student.Id && p.LessonId == l.Id, ct))
            {
                db.LessonProgress.Add(new LessonProgress { StudentId = ctx.Student.Id, LessonId = l.Id, ChapterId = l.ChapterId, SubjectId = l.SubjectId, CompletedAtUtc = clock.UtcNow });
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear(); // double tap: the unique index already holds this completion
                }
            }

            var awarded = await xp.AwardAsync(new XpAwardPolicy.LearningEvent("lesson-completed", ctx.Student.Id, l.Id), ct);
            var chapterLessons = await access.VisibleLessons(ctx.ClassId).Where(x => x.ChapterId == l.ChapterId).Select(x => x.Id).ToListAsync(ct);
            var doneInChapter = await db.LessonProgress.CountAsync(p => p.StudentId == ctx.Student.Id && chapterLessons.Contains(p.LessonId), ct);
            var chapterDone = chapterLessons.Count > 0 && doneInChapter >= chapterLessons.Count;
            if (chapterDone)
            {
                awarded += await xp.AwardAsync(new XpAwardPolicy.LearningEvent("chapter-completed", ctx.Student.Id, l.ChapterId), ct);
            }
            var before = ctx.Student.TotalXp;
            var streak = await activity.RecordAsync(ctx.Student, ct);
            var total = await db.StudentProfiles.Where(s => s.Id == ctx.Student.Id).Select(s => s.TotalXp).FirstAsync(ct);
            var newBadges = await badges.EvaluateAsync(ctx.Student.Id, ct);
            return Results.Ok(new CompleteLessonResult(Math.Max(awarded, total - before), chapterDone, streak, total, newBadges));
        });
    }

    private static void MapAdmin(RouteGroupBuilder g)
    {
        g.MapGet("/chapters/{chapterId:guid}/lessons", async (Guid chapterId, GoogolsDbContext db, CancellationToken ct) =>
            await db.Lessons.AsNoTracking().Where(l => l.ChapterId == chapterId).OrderBy(l => l.Sequence)
                .Select(l => new AdminLessonDto(l.Id, l.Title, l.Sequence, l.Language, l.Kind.ToString(), l.EstimatedMinutes, l.Status.ToString(),
                    l.Body.Length, l.AttachmentFileName, l.AttachmentBytes, l.UpdatedAtUtc)).ToListAsync(ct));

        g.MapGet("/lessons/{lessonId:guid}", async (Guid lessonId, GoogolsDbContext db, CancellationToken ct) =>
            await db.Lessons.AsNoTracking().Where(l => l.Id == lessonId)
                .Select(l => new AdminLessonDetailDto(l.Id, l.ChapterId, l.Title, l.Sequence, l.Body, l.Language, l.Kind.ToString(), l.EstimatedMinutes,
                    l.Status.ToString(), l.AttachmentFileName, l.AttachmentBytes)).FirstOrDefaultAsync(ct) is { } dto
                ? Results.Ok(dto) : Results.NotFound());

        g.MapPost("/chapters/{chapterId:guid}/lessons", async (Guid chapterId, LessonUpsertRequest req, IValidator<LessonUpsertRequest> v,
            GoogolsDbContext db, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            var chapter = await db.Chapters.AsNoTracking().Where(c => c.Id == chapterId)
                .Select(c => new { c.Id, c.Subject!.MediumLanguage }).FirstOrDefaultAsync(ct);
            if (chapter is null)
            {
                return Results.NotFound();
            }
            var next = (await db.Lessons.Where(l => l.ChapterId == chapterId).MaxAsync(l => (int?)l.Sequence, ct) ?? 0) + 1;
            var lesson = new Lesson
            {
                ChapterId = chapterId,
                Title = req.Title.Trim(),
                Body = req.Body.Trim(),
                Language = (req.Language ?? chapter.MediumLanguage).ToLowerInvariant(),
                Kind = Enum.Parse<ContentKind>(req.Kind),
                EstimatedMinutes = req.EstimatedMinutes,
                Sequence = req.Sequence ?? next,
            };
            db.Lessons.Add(lesson);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/curriculum/admin/lessons/{lesson.Id}", new { lesson.Id });
        });

        g.MapPut("/lessons/{lessonId:guid}", async (Guid lessonId, LessonUpsertRequest req, IValidator<LessonUpsertRequest> v,
            GoogolsDbContext db, LessonIndexer indexer, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
            if (lesson is null)
            {
                return Results.NotFound();
            }
            lesson.Title = req.Title.Trim();
            lesson.Body = req.Body.Trim();
            lesson.Language = (req.Language ?? lesson.Language).ToLowerInvariant();
            lesson.Kind = Enum.Parse<ContentKind>(req.Kind);
            lesson.EstimatedMinutes = req.EstimatedMinutes;
            lesson.Sequence = req.Sequence ?? lesson.Sequence;
            await db.SaveChangesAsync(ct);
            await indexer.ReindexAsync(lesson, ct); // published edits reach the AI teacher immediately
            return Results.NoContent();
        });

        g.MapPut("/lessons/{lessonId:guid}/status", async (Guid lessonId, Application.Curriculum.SetStatusRequest req,
            GoogolsDbContext db, LessonIndexer indexer, CancellationToken ct) =>
        {
            if (!Application.Curriculum.PublicationStatusParser.TryParseAdminStatus(req.Status, out var status))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Choose Draft, Published or Archived."] });
            }
            var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
            if (lesson is null)
            {
                return Results.NotFound();
            }
            lesson.Status = status;
            await db.SaveChangesAsync(ct);
            await indexer.ReindexAsync(lesson, ct);
            return Results.NoContent();
        });

        g.MapDelete("/lessons/{lessonId:guid}", async (Guid lessonId, GoogolsDbContext db, IClock clock, LessonIndexer indexer, CancellationToken ct) =>
        {
            var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
            if (lesson is null)
            {
                return Results.NotFound();
            }
            // Archive instead of hard delete: students' progress rows keep pointing at it.
            lesson.ArchivedAtUtc = clock.UtcNow;
            lesson.Status = PublicationStatus.Archived;
            await db.SaveChangesAsync(ct);
            await indexer.ReindexAsync(lesson, ct);
            return Results.NoContent();
        });

        g.MapPost("/lessons/{lessonId:guid}/attachment", async (Guid lessonId, HttpRequest http, GoogolsDbContext db, IFileStorage files, CancellationToken ct) =>
        {
            // The form is read by hand (no IFormFile binding). Cookies are SameSite=Lax and CORS is locked to the app's
            // origin, so a cross-site form post can't carry the admin's session.
            if (!http.HasFormContentType)
            {
                return Results.BadRequest(new { message = "Send the file as multipart/form-data." });
            }
            var form = await http.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Choose a PDF, PNG or JPEG file."] });
            }
            if (file.Length > AttachmentRules.MaxBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Files must be 20 MB or smaller."] });
            }
            var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
            if (lesson is null)
            {
                return Results.NotFound();
            }
            await using var stream = file.OpenReadStream();
            var head = new byte[8];
            var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
            var type = AttachmentRules.DetectContentType(head.AsSpan(0, read));
            if (type is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Only PDF, PNG and JPEG files are allowed."] });
            }
            stream.Position = 0;
            var name = AttachmentRules.SafeFileName(file.FileName, type);
            var blob = await files.SaveAsync(AttachmentContainer, name, stream, type, ct);
            var old = lesson.AttachmentBlobName;
            lesson.AttachmentBlobName = blob;
            lesson.AttachmentFileName = name;
            lesson.AttachmentContentType = type;
            lesson.AttachmentBytes = file.Length;
            await db.SaveChangesAsync(ct);
            if (old is not null)
            {
                await files.DeleteAsync(AttachmentContainer, old, ct);
            }
            return Results.Ok(new { fileName = name, bytes = file.Length });
        }).DisableAntiforgery();

        g.MapDelete("/lessons/{lessonId:guid}/attachment", async (Guid lessonId, GoogolsDbContext db, IFileStorage files, CancellationToken ct) =>
        {
            var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct);
            if (lesson?.AttachmentBlobName is not { } blob)
            {
                return Results.NotFound();
            }
            lesson.AttachmentBlobName = lesson.AttachmentFileName = lesson.AttachmentContentType = null;
            lesson.AttachmentBytes = null;
            await db.SaveChangesAsync(ct);
            await files.DeleteAsync(AttachmentContainer, blob, ct);
            return Results.NoContent();
        });
    }
}
