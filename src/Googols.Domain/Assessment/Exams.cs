using Googols.Domain.Common;

namespace Googols.Domain.Assessment;

public enum ExamKind
{
    Practice = 0,      // a timed test a student builds for themselves
    Mock = 1,          // a mock exam published by the content team for a whole class
    ClassTest = 2,     // a timed test a teacher sets for one classroom
}

/// <summary>
/// A timed test: multiple-choice questions drawn from the chosen chapters, plus optional written questions.
/// Each attempt freezes its own question set, so later bank edits never change past results.
/// </summary>
public class Exam : Entity, ISoftDeletable
{
    public const int MaxMcq = 50;
    public const int MaxWritten = 5;
    public const int MaxMinutes = 180;

    public Guid ClassId { get; set; }
    public Guid? SubjectId { get; set; }
    public required string Title { get; set; }
    public ExamKind Kind { get; set; }
    public required string ChapterIdsJson { get; set; }
    public int McqCount { get; set; }
    public int WrittenCount { get; set; }
    public int DurationMinutes { get; set; }
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;
    public DateTime? OpensAtUtc { get; set; }
    public DateTime? ClosesAtUtc { get; set; }
    public Guid? CreatedForStudentId { get; set; }     // Practice tests belong to one student
    public Guid? ClassroomId { get; set; }             // ClassTest belongs to one classroom
    public DateTime? ArchivedAtUtc { get; set; }
}

public class ExamAttempt : Entity
{
    /// <summary>Answers saved up to this long after the deadline still count (network delays, slow phones).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(60);

    public Guid ExamId { get; set; }
    public Guid StudentId { get; set; }
    public required string QuestionIdsJson { get; set; }
    public required string WrittenIdsJson { get; set; }
    public string? AnswersJson { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime DeadlineUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public int McqScore { get; set; }
    public int McqTotal { get; set; }
    public double? WrittenScore { get; set; }
    public int WrittenMax { get; set; }
    public double? Percent { get; set; }
}

/// <summary>A question answered in the student's own words, marked against a rubric (criteria with marks).</summary>
public class WrittenQuestion : Entity, ISoftDeletable
{
    public Guid ChapterId { get; set; }
    public required string Prompt { get; set; }
    public string? ModelAnswer { get; set; }
    public required string RubricJson { get; set; }    // [{"criterion":"Names chlorophyll","marks":1}, ...]
    public int MaxMarks { get; set; }
    public string Language { get; set; } = LanguageCodes.English;
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;
    public DateTime? ArchivedAtUtc { get; set; }
}

public enum MarkingStatus
{
    Provisional = 0,   // marked by the AI; the student sees it labelled as provisional
    Confirmed = 1,     // checked (and possibly changed) by a teacher
    NotMarked = 2,     // the AI could not mark it; waits for a teacher
}

public class WrittenAnswer : Entity
{
    public Guid AttemptId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid StudentId { get; set; }
    public required string AnswerText { get; set; }
    public int MaxMarks { get; set; }
    public double? AiScore { get; set; }
    public string? AiFeedbackJson { get; set; }
    public double? TeacherScore { get; set; }
    public string? TeacherComment { get; set; }
    public Guid? MarkedByUserId { get; set; }
    public MarkingStatus Status { get; set; }

    public double? FinalScore => TeacherScore ?? AiScore;
}
