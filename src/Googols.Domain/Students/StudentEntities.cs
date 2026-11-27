using Googols.Domain.Common;

namespace Googols.Domain.Students;

public class StudentProfile : Entity, IConcurrencyTracked, ISchoolScoped
{
    public Guid UserId { get; set; }                   // Identity user that signs in as this student
    public required string DisplayName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string PreferredLanguage { get; set; } = LanguageCodes.English;
    public Guid? SchoolId { get; set; }
    public int TotalXp { get; set; }                   // cache of the XP ledger sum; only the server writes it
    public int CurrentStreakDays { get; set; }
    public DateOnly? LastActiveLocalDate { get; set; }
    public bool ShowOnLeaderboard { get; set; } = true;   // students can hide themselves from the class leaderboard
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Under-18 accounts need verified parental consent before AI features or data sharing are enabled.</summary>
    public bool RequiresParentalConsent(DateOnly today) =>
        DateOfBirth is null || DateOfBirth.Value.AddYears(18) > today;
}

public class Enrollment : Entity
{
    public Guid StudentId { get; set; }
    public StudentProfile? Student { get; set; }
    public Guid CurriculumId { get; set; }
    public Guid ClassId { get; set; }
    public required string AcademicYear { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum ParentLinkStatus
{
    Pending = 0,
    Verified = 1,
    Revoked = 2,
}

/// <summary>A parent sees a student's data only while this link is <see cref="ParentLinkStatus.Verified"/>.</summary>
public class ParentStudentLink : Entity
{
    public Guid ParentUserId { get; set; }
    public Guid StudentId { get; set; }
    public StudentProfile? Student { get; set; }
    public required string Relationship { get; set; }
    public ParentLinkStatus Status { get; set; } = ParentLinkStatus.Pending;
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? ConsentGrantedAtUtc { get; set; }
    public string? ConsentVersion { get; set; }
    public bool WeeklyReportEmail { get; set; } = true;
}

/// <summary>One weekly report email sent to a parent about a child. Unique per week, so it's never sent twice.</summary>
public class ParentReportLog : Entity
{
    public Guid ParentUserId { get; set; }
    public Guid StudentId { get; set; }
    public DateOnly WeekStart { get; set; }
    public DateTime SentAtUtc { get; set; }
}
