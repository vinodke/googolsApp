using Googols.Domain.Common;

namespace Googols.Domain.Curricula;

// Hierarchy: Board -> Curriculum (versioned per academic year) -> Class -> Subject -> Chapter -> Topic -> LearningObjective.
// Nothing here is hardcoded: every level is data, imported from official syllabus sources and approved before publishing.

public class CurriculumBoard : Entity, ISoftDeletable
{
    public required string Name { get; set; }          // "Kerala State (SCERT)", "CBSE", "ICSE"
    public required string Code { get; set; }          // "KL-SCERT", "CBSE"
    public required string CountryCode { get; set; }   // ISO 3166-1 alpha-2, e.g. "IN"
    public bool IsEntranceExam { get; set; }           // NEET, JEE, KEAM, CUET are boards with this flag
    public DateTime? ArchivedAtUtc { get; set; }
    public List<Curriculum> Curricula { get; } = [];
}

public class Curriculum : Entity, ISoftDeletable
{
    public Guid BoardId { get; set; }
    public CurriculumBoard? Board { get; set; }
    public required string Name { get; set; }
    public required string AcademicYear { get; set; }  // "2026-27"
    public int Version { get; set; } = 1;
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;
    public DateTime? ArchivedAtUtc { get; set; }
    public List<AcademicClass> Classes { get; } = [];
}

public class AcademicClass : Entity
{
    public Guid CurriculumId { get; set; }
    public Curriculum? Curriculum { get; set; }
    public int GradeNumber { get; set; }               // 1..12; entrance tracks use 0
    public required string GradeName { get; set; }     // "Class 8", "Standard 8"
    public List<Subject> Subjects { get; } = [];
}

public class Subject : Entity
{
    public Guid ClassId { get; set; }
    public AcademicClass? Class { get; set; }
    public required string Name { get; set; }
    public required string Code { get; set; }
    public required string MediumLanguage { get; set; } // language the textbook is written in
    public int Sequence { get; set; }
    public List<Chapter> Chapters { get; } = [];
}

public class Chapter : Entity
{
    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public required string Title { get; set; }
    public int Sequence { get; set; }
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;
    public List<Topic> Topics { get; } = [];
}

public class Topic : Entity
{
    public Guid ChapterId { get; set; }
    public Chapter? Chapter { get; set; }
    public required string Title { get; set; }
    public int Sequence { get; set; }
    public List<LearningObjective> Objectives { get; } = [];
}

public class LearningObjective : Entity
{
    public Guid TopicId { get; set; }
    public Topic? Topic { get; set; }
    public required string Description { get; set; }
}
