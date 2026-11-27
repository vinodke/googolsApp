using Googols.Domain.Common;

namespace Googols.Domain.Schools;

/// <summary>
/// A teacher's group of students for one academic class (e.g. "8B Biology"). Students join with a code
/// from the teacher; the teacher then sees their progress. Students can leave at any time.
/// </summary>
public class Classroom : Entity, ISoftDeletable
{
    public Guid TeacherUserId { get; set; }
    public Guid ClassId { get; set; }                  // AcademicClass the students must be enrolled in
    public required string Name { get; set; }
    public string? SchoolName { get; set; }
    public required string JoinCode { get; set; }      // short, shown to the class; can be rotated
    public bool JoiningOpen { get; set; } = true;
    public DateTime? ArchivedAtUtc { get; set; }
    public Guid? SchoolId { get; set; }                // set for classrooms created from a school allocation
    public Guid? DivisionId { get; set; }              // members follow the division's members
    public Guid? SubjectId { get; set; }
}

public class ClassroomMember : Entity
{
    public Guid ClassroomId { get; set; }
    public Guid StudentId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}

/// <summary>A school using Googols. Its admins manage divisions, staff and timetables; nobody outside the school sees its data.</summary>
public class School : Entity, ISoftDeletable
{
    public required string Name { get; set; }
    public required string Code { get; set; }          // short unique code, e.g. "GHSS-KTM"
    public string? City { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
}

public static class SchoolRoles
{
    public const string Admin = "SchoolAdmin";
    public const string Teacher = "Teacher";
}

public class SchoolMember : Entity
{
    public Guid SchoolId { get; set; }
    public Guid UserId { get; set; }
    public required string Role { get; set; }          // SchoolRoles.Admin | SchoolRoles.Teacher
}

/// <summary>A division (section) of a class in one school, e.g. "8A". Students join it with its code.</summary>
public class Division : Entity, ISoftDeletable
{
    public Guid SchoolId { get; set; }
    public Guid ClassId { get; set; }                  // AcademicClass (board + year + grade)
    public required string Name { get; set; }
    public required string JoinCode { get; set; }
    public bool JoiningOpen { get; set; } = true;
    public DateTime? ArchivedAtUtc { get; set; }
}

public class DivisionMember : Entity
{
    public Guid DivisionId { get; set; }
    public Guid StudentId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}

/// <summary>A teacher teaching one subject to one division. Backed by a classroom so all teacher tools work for it.</summary>
public class TeacherAllocation : Entity
{
    public Guid DivisionId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid TeacherUserId { get; set; }
    public Guid ClassroomId { get; set; }
}

public class TimetableSlot : Entity
{
    public const int MaxPeriods = 10;

    public Guid DivisionId { get; set; }
    public int DayOfWeek { get; set; }                 // 1 = Monday … 6 = Saturday
    public int Period { get; set; }                    // 1 … MaxPeriods
    public Guid? SubjectId { get; set; }
    public Guid? TeacherUserId { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Label { get; set; }                 // e.g. "Lunch", "PT", when there's no subject
}

public enum AssignmentKind
{
    Lesson = 0,    // read a lesson
    Quiz = 1,      // take a chapter quiz
    Test = 2,      // take one of the classroom's timed tests
    Task = 3,      // anything else; the student marks it done (with an optional note)
}

/// <summary>Homework a teacher sets for a classroom. Completion is checked from real activity where possible.</summary>
public class Assignment : Entity, ISoftDeletable
{
    public Guid ClassroomId { get; set; }
    public required string Title { get; set; }
    public string? Instructions { get; set; }
    public AssignmentKind Kind { get; set; }
    public Guid? LessonId { get; set; }
    public Guid? ChapterId { get; set; }
    public Guid? ExamId { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
}

public class AssignmentSubmission : Entity
{
    public Guid AssignmentId { get; set; }
    public Guid StudentId { get; set; }
    public string? Text { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}
