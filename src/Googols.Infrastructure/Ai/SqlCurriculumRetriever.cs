using Googols.Application.Ai;
using Googols.Domain.Common;
using Googols.Domain.Content;
using Googols.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Googols.Infrastructure.Ai;

/// <summary>
/// Default retriever: ranks the subject's published lesson chunks stored in SQL with BM25 (<see cref="KeywordRanker"/>).
/// No extra Azure service needed. Chunks only exist for Published lessons, and the chapter must be Published too.
/// When the student is on a chapter and nothing matches the words of the question, the chapter's opening
/// chunks are used so the teacher still answers from the textbook.
/// </summary>
public sealed class SqlCurriculumRetriever(GoogolsDbContext db) : ICurriculumRetriever
{
    private const int CandidateLimit = 600;

    public async Task<IReadOnlyList<RetrievedPassage>> SearchAsync(StudentLearningContext c, string query, int top, CancellationToken ct)
    {
        var q = from ch in db.ContentChunks.AsNoTracking()
                join chapter in db.Chapters on ch.ChapterId equals chapter.Id
                where ch.SubjectId == c.SubjectId && chapter.Status == PublicationStatus.Published
                select ch;
        if (c.ChapterId is { } chapterId)
        {
            q = q.Where(ch => ch.ChapterId == chapterId);
        }
        var rows = await q.OrderBy(ch => ch.ChapterId).ThenBy(ch => ch.Sequence).Take(CandidateLimit)
            .Select(ch => new { ch.LessonId, ch.Title, ch.Text, ch.Kind }).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return [];
        }

        var ranked = KeywordRanker.Rank(query, rows.Select(r => r.Text).ToList(), top);
        var picked = ranked.Count > 0
            ? ranked.Select(r => (Row: rows[r.Index], r.Score))
            : c.ChapterId is not null ? rows.Take(Math.Min(top, 3)).Select(r => (Row: r, Score: 0.0)) : [];

        return picked.Select(p => new RetrievedPassage(p.Row.LessonId, p.Row.Title, p.Row.Text,
            p.Row.Kind == ContentKind.OfficialSyllabus ? SourceKind.OfficialSyllabus : SourceKind.ApprovedMaterial, p.Score)).ToList();
    }
}
