using FluentValidation;
using Googols.Application.Abstractions;
using Googols.Application.Curriculum;
using Googols.Application.Gamification;
using Googols.Domain.Common;
using Googols.Domain.Students;
using Googols.Infrastructure.Identity;
using Googols.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>
/// Student self-service. Routes never take a student id: the profile is always resolved from the
/// signed-in user, so changing an id in a request cannot expose another child's data.
/// </summary>
public static class StudentEndpoints
{
    public sealed record StudentMeDto(Guid Id, string DisplayName, string PreferredLanguage, int TotalXp, int Level, int StreakDays,
        EnrollmentDto? Enrollment, bool ConsentRequired);
    public sealed record EnrollmentDto(Guid CurriculumId, string CurriculumName, string AcademicYear, Guid ClassId, string ClassName, string BoardCode);

    public static void MapStudentEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/students").WithTags("Students").RequireAuthorization();

        g.MapGet("/me", async (ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var student = await db.StudentProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == me.UserId, ct);
            if (student is null)
            {
                return Results.NotFound(new { message = "No student profile yet. Complete onboarding." });
            }
            var enrollment = await (
                from e in db.Enrollments.AsNoTracking()
                join c in db.Curricula on e.CurriculumId equals c.Id
                join b in db.CurriculumBoards on c.BoardId equals b.Id
                join k in db.AcademicClasses on e.ClassId equals k.Id
                where e.StudentId == student.Id && e.IsActive
                select new EnrollmentDto(c.Id, c.Name, c.AcademicYear, k.Id, k.GradeName, b.Code)).FirstOrDefaultAsync(ct);

            var consentRequired = student.RequiresParentalConsent(clock.TodayIn("Asia/Kolkata"))
                && !await db.ParentStudentLinks.AnyAsync(l => l.StudentId == student.Id && l.Status == ParentLinkStatus.Verified && l.ConsentGrantedAtUtc != null, ct);

            return Results.Ok(new StudentMeDto(student.Id, student.DisplayName, student.PreferredLanguage, student.TotalXp,
                XpAwardPolicy.LevelFor(student.TotalXp), student.CurrentStreakDays, enrollment, consentRequired));
        });

        g.MapPost("/me/onboarding", async (StudentOnboardingRequest req, IValidator<StudentOnboardingRequest> v, ICurrentUser me,
            GoogolsDbContext db, UserManager<AppUser> users, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            if (me.UserId is not { } userId)
            {
                return Results.Unauthorized();
            }
            if (await db.StudentProfiles.AnyAsync(s => s.UserId == userId, ct))
            {
                return Results.Conflict(new { message = "Onboarding is already complete." });
            }

            var cls = await db.AcademicClasses.AsNoTracking()
                .Where(c => c.Id == req.ClassId && c.CurriculumId == req.CurriculumId)
                .Select(c => new { c.Id, c.Curriculum!.AcademicYear, c.Curriculum.Status })
                .FirstOrDefaultAsync(ct);
            if (cls is null || cls.Status != PublicationStatus.Published)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["classId"] = ["That class is not available for this curriculum."] });
            }

            var student = new StudentProfile
            {
                UserId = userId,
                DisplayName = req.DisplayName.Trim(),
                DateOfBirth = req.DateOfBirth,
                PreferredLanguage = req.PreferredLanguage.ToLowerInvariant(),
            };
            db.StudentProfiles.Add(student);
            db.Enrollments.Add(new Enrollment { StudentId = student.Id, CurriculumId = req.CurriculumId, ClassId = cls.Id, AcademicYear = cls.AcademicYear });
            await db.SaveChangesAsync(ct);

            var user = await users.FindByIdAsync(userId.ToString());
            if (user is not null && !await users.IsInRoleAsync(user, Roles.Student))
            {
                await users.AddToRoleAsync(user, Roles.Student);
            }
            return Results.Created("/api/v1/students/me", new { student.Id });
        });

        g.MapPut("/me/language", async (LanguageRequest req, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!LanguageCodes.IsSupported(req.Language))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["language"] = ["Choose en or ml."] });
            }
            var student = await db.StudentProfiles.FirstOrDefaultAsync(s => s.UserId == me.UserId, ct);
            if (student is null)
            {
                return Results.NotFound();
            }
            student.PreferredLanguage = req.Language.ToLowerInvariant();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public sealed record LanguageRequest(string Language);
}
