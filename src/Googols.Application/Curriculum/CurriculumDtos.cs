using FluentValidation;
using Googols.Domain.Common;

namespace Googols.Application.Curriculum;

public sealed record BoardDto(Guid Id, string Name, string Code, string CountryCode, bool IsEntranceExam);
public sealed record ClassDto(Guid Id, int GradeNumber, string GradeName);
public sealed record SubjectDto(Guid Id, string Name, string Code, string MediumLanguage);
public sealed record ChapterDto(Guid Id, string Title, int Sequence);
public sealed record TopicDto(Guid Id, string Title, int Sequence);

public sealed record CreateBoardRequest(string Name, string Code, string CountryCode, bool IsEntranceExam);
public sealed record CreateChapterRequest(Guid SubjectId, string Title, int Sequence);

public sealed class CreateBoardValidator : AbstractValidator<CreateBoardRequest>
{
    public CreateBoardValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40).Matches("^[A-Z0-9-]+$")
            .WithMessage("Code uses capital letters, digits and hyphens, e.g. KL-SCERT.");
        RuleFor(x => x.CountryCode).NotEmpty().Length(2);
    }
}

public sealed class CreateChapterValidator : AbstractValidator<CreateChapterRequest>
{
    public CreateChapterValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Sequence).InclusiveBetween(1, 500);
    }
}

public sealed record StudentOnboardingRequest(string DisplayName, DateOnly? DateOfBirth, string PreferredLanguage, Guid CurriculumId, Guid ClassId);

public sealed class StudentOnboardingValidator : AbstractValidator<StudentOnboardingRequest>
{
    public StudentOnboardingValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PreferredLanguage).Must(LanguageCodes.IsSupported).WithMessage("Choose English (en) or Malayalam (ml).");
        RuleFor(x => x.CurriculumId).NotEmpty();
        RuleFor(x => x.ClassId).NotEmpty();
        RuleFor(x => x.DateOfBirth).Must(d => d is null || d.Value.Year >= 1990)
            .WithMessage("Enter a valid date of birth.");
    }
}
