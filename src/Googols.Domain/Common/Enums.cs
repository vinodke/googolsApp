namespace Googols.Domain.Common;

/// <summary>Content lifecycle. Only <see cref="Published"/> content is ever shown to students or used by AI retrieval.</summary>
public enum PublicationStatus
{
    Draft = 0,
    InReview = 1,
    Approved = 2,
    Published = 3,
    Archived = 4,
}

/// <summary>Teaching languages. Stored as ISO 639-1 codes so new languages need no schema change.</summary>
public static class LanguageCodes
{
    public const string English = "en";
    public const string Malayalam = "ml";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        English,
        Malayalam,
    };

    public static bool IsSupported(string? code) => code is not null && Supported.Contains(code);
}

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string ContentManager = "ContentManager";
    public const string Student = "Student";
    public const string Parent = "Parent";
    public const string Teacher = "Teacher";
    public const string SchoolAdmin = "SchoolAdmin";

    public static readonly string[] All = [SuperAdmin, Admin, ContentManager, Student, Parent, Teacher, SchoolAdmin];
}
