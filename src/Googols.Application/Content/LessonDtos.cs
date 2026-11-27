using FluentValidation;
using Googols.Domain.Common;
using Googols.Domain.Content;

namespace Googols.Application.Content;

public sealed record LessonUpsertRequest(string Title, string Body, string? Language, int EstimatedMinutes, string Kind, int? Sequence);

public sealed record AdminLessonDto(Guid Id, string Title, int Sequence, string Language, string Kind, int EstimatedMinutes, string Status,
    int BodyLength, string? AttachmentFileName, long? AttachmentBytes, DateTime? UpdatedAtUtc);

public sealed record AdminLessonDetailDto(Guid Id, Guid ChapterId, string Title, int Sequence, string Body, string Language, string Kind,
    int EstimatedMinutes, string Status, string? AttachmentFileName, long? AttachmentBytes);

public sealed record StudentLessonItemDto(Guid Id, string Title, int Sequence, int EstimatedMinutes, bool Completed, bool HasAttachment);

public sealed record StudentChapterDto(Guid Id, string Title, int Sequence, Guid SubjectId, string SubjectName, IReadOnlyList<StudentLessonItemDto> Lessons);

public sealed record StudentLessonDto(Guid Id, string Title, string Body, string Language, string Kind, int EstimatedMinutes,
    Guid ChapterId, string ChapterTitle, Guid SubjectId, string SubjectName, bool Completed,
    string? AttachmentFileName, long? AttachmentBytes, Guid? PreviousLessonId, Guid? NextLessonId);

public sealed record ChapterProgressDto(Guid Id, string Title, int Sequence, int TotalLessons, int CompletedLessons);

public sealed record SubjectProgressDto(Guid SubjectId, string Name, string Code, int TotalLessons, int CompletedLessons);

public sealed record LearningSummaryDto(int TotalLessons, int CompletedLessons, int StreakDays, IReadOnlyList<SubjectProgressDto> Subjects);

public sealed record CompleteLessonResult(int XpAwarded, bool ChapterCompleted, int StreakDays, int TotalXp);

public sealed class LessonUpsertValidator : AbstractValidator<LessonUpsertRequest>
{
    public const int MaxBody = 100_000;

    public LessonUpsertValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(MaxBody).WithMessage($"Lesson text must be under {MaxBody:N0} characters. Split it into two lessons.");
        RuleFor(x => x.Language).Must(l => l is null || LanguageCodes.IsSupported(l)).WithMessage("Choose en or ml.");
        RuleFor(x => x.EstimatedMinutes).InclusiveBetween(1, 180);
        RuleFor(x => x.Kind).Must(k => Enum.TryParse<ContentKind>(k, ignoreCase: false, out _)).WithMessage("Choose OfficialSyllabus or ApprovedMaterial.");
        RuleFor(x => x.Sequence).InclusiveBetween(1, 999).When(x => x.Sequence is not null);
    }
}

/// <summary>Attachments are checked by their first bytes, not by the name or the browser's content type.</summary>
public static class AttachmentRules
{
    public const long MaxBytes = 20 * 1024 * 1024;

    public static string? DetectContentType(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }
        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }))
        {
            return "image/png";
        }
        if (head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }
        return null;
    }

    public static string SafeFileName(string? name, string contentType)
    {
        var baseName = Path.GetFileNameWithoutExtension(name ?? "file");
        var clean = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "file";
        }
        if (clean.Length > 80)
        {
            clean = clean[..80];
        }
        var ext = contentType switch { "application/pdf" => ".pdf", "image/png" => ".png", _ => ".jpg" };
        return clean + ext;
    }
}
