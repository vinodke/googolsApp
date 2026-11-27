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
}

public class ClassroomMember : Entity
{
    public Guid ClassroomId { get; set; }
    public Guid StudentId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}
