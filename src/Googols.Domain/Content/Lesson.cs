using Googols.Domain.Common;
using Googols.Domain.Curricula;

namespace Googols.Domain.Content;

/// <summary>Where lesson text came from. Shown to students next to AI answers that use it.</summary>
public enum ContentKind
{
    OfficialSyllabus = 0,   // text from the official textbook / syllabus
    ApprovedMaterial = 1,   // teacher-written notes reviewed by a content manager
}

/// <summary>
/// A readable lesson inside a chapter. Students and the AI teacher only see it when it is Published
/// and its chapter is Published. Optional PDF/image attachment lives in Blob storage.
/// </summary>
public class Lesson : Entity, ISoftDeletable
{
    public Guid ChapterId { get; set; }
    public Chapter? Chapter { get; set; }
    public required string Title { get; set; }
    public int Sequence { get; set; }
    public required string Body { get; set; }              // plain text / light markdown (paragraphs, "## headings", "- lists")
    public string Language { get; set; } = LanguageCodes.English;
    public ContentKind Kind { get; set; } = ContentKind.ApprovedMaterial;
    public int EstimatedMinutes { get; set; } = 10;
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;
    public string? AttachmentBlobName { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentBytes { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
}

/// <summary>
/// Retrieval unit for the AI teacher. Rows exist only for Published lessons: they are rebuilt when a
/// lesson is published or edited, and deleted when it is unpublished, so draft text can't leak into answers.
/// </summary>
public class ContentChunk : Entity
{
    public Guid LessonId { get; set; }
    public Guid ChapterId { get; set; }
    public Guid SubjectId { get; set; }
    public int Sequence { get; set; }
    public required string Text { get; set; }
    public required string Title { get; set; }
    public ContentKind Kind { get; set; }
}

/// <summary>A student finished a lesson. One row per (student, lesson); completion is idempotent.</summary>
public class LessonProgress : Entity
{
    public Guid StudentId { get; set; }
    public Guid LessonId { get; set; }
    public Guid ChapterId { get; set; }
    public Guid SubjectId { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}
