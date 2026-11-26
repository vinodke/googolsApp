using FluentValidation;
using Googols.Domain.Common;

namespace Googols.Application.Curriculum;

// Admin view of the whole catalog, including drafts. Students never receive this shape.
public sealed record AdminChapterDto(Guid Id, string Title, int Sequence, string Status);
public sealed record AdminSubjectDto(Guid Id, string Name, string Code, string MediumLanguage, int Sequence, IReadOnlyList<AdminChapterDto> Chapters);
public sealed record AdminClassDto(Guid Id, int GradeNumber, string GradeName, IReadOnlyList<AdminSubjectDto> Subjects);
public sealed record AdminCurriculumDto(Guid Id, string Name, string AcademicYear, int Version, string Status, IReadOnlyList<AdminClassDto> Classes);
public sealed record AdminBoardDto(Guid Id, string Name, string Code, string CountryCode, bool IsEntranceExam, IReadOnlyList<AdminCurriculumDto> Curricula);

public sealed record CreateCurriculumRequest(Guid BoardId, string Name, string AcademicYear);
public sealed record CreateClassRequest(Guid CurriculumId, int GradeNumber, string GradeName);
public sealed record CreateSubjectRequest(Guid ClassId, string Name, string Code, string MediumLanguage, int Sequence);
public sealed record SetStatusRequest(string Status);

public sealed class CreateCurriculumValidator : AbstractValidator<CreateCurriculumRequest>
{
    public CreateCurriculumValidator()
    {
        RuleFor(x => x.BoardId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AcademicYear).NotEmpty().Matches(@"^\d{4}-\d{2}$").WithMessage("Use the form 2026-27.");
    }
}

public sealed class CreateClassValidator : AbstractValidator<CreateClassRequest>
{
    public CreateClassValidator()
    {
        RuleFor(x => x.CurriculumId).NotEmpty();
        RuleFor(x => x.GradeNumber).InclusiveBetween(0, 12).WithMessage("Class must be 1 to 12 (0 for entrance courses).");
        RuleFor(x => x.GradeName).NotEmpty().MaximumLength(50);
    }
}

public sealed class CreateSubjectValidator : AbstractValidator<CreateSubjectRequest>
{
    public CreateSubjectValidator()
    {
        RuleFor(x => x.ClassId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40).Matches("^[A-Za-z0-9-]+$").WithMessage("Code uses letters, digits and hyphens, e.g. BIO-8.");
        RuleFor(x => x.MediumLanguage).Must(LanguageCodes.IsSupported).WithMessage("Medium must be en or ml.");
        RuleFor(x => x.Sequence).InclusiveBetween(1, 100);
    }
}

public static class PublicationStatusParser
{
    /// <summary>Admins can move content between Draft, Published and Archived from the catalog screen.</summary>
    public static bool TryParseAdminStatus(string? value, out PublicationStatus status) =>
        Enum.TryParse(value, ignoreCase: true, out status)
        && status is PublicationStatus.Draft or PublicationStatus.Published or PublicationStatus.Archived;
}
