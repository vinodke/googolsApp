using System.Text.Encodings.Web;
using Googols.Application.Abstractions;
using Googols.Application.Common;
using Googols.Application.Gamification;
using Googols.Domain.Common;
using Googols.Domain.Students;
using Googols.Infrastructure.Identity;
using Googols.Infrastructure.Persistence;
using Googols.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>
/// Parent ↔ student linking and verifiable parental consent (DPDP Act 2023: under-18s need a parent's consent).
/// A student creates a one-time code; a parent signed in with their own account enters it, which links the
/// accounts; the parent then reads the consent notice and grants (or later withdraws) consent.
/// </summary>
public static class ParentEndpoints
{
    public sealed record ParentCodeDto(string Code, DateTime ExpiresAtUtc);
    public sealed record StudentParentDto(Guid LinkId, string Relationship, string ParentEmail, string Status, bool ConsentGranted);
    public sealed record LinkRequest(string Code, string Relationship);
    public sealed record ConsentRequest(bool Accept, string ConsentVersion);
    public sealed record ChildDto(Guid StudentId, string DisplayName, string? ClassName, string? BoardCode, string? AcademicYear,
        int Level, int TotalXp, int StreakDays, DateOnly? LastActiveDate, bool ConsentRequired, bool ConsentGranted,
        string? ConsentVersion, DateTime? ConsentGrantedAtUtc, IReadOnlyList<RecentXpDto> RecentXp);
    public sealed record RecentXpDto(int Points, string Reason, DateTime CreatedAtUtc);

    private static readonly string[] Relationships = ["mother", "father", "guardian", "other"];

    public static void MapParentEndpoints(this RouteGroupBuilder v1)
    {
        // ---- Student side
        var s = v1.MapGroup("/students/me").WithTags("Parents").RequireAuthorization(Plumbing.Policies.Student);

        s.MapPost("/parent-code", async (ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var student = await db.StudentProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == me.UserId, ct);
            if (student is null)
            {
                return Results.NotFound();
            }
            var now = clock.UtcNow;
            // Only the newest code works: retire any earlier unused ones.
            await db.ParentLinkCodes.Where(c => c.StudentId == student.Id && c.UsedAtUtc == null && c.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ExpiresAtUtc, now), ct);
            var code = ParentLinkCode.NewCode();
            var row = new ParentLinkCode { StudentId = student.Id, CodeHash = ParentLinkCode.Hash(code), ExpiresAtUtc = now + ParentLinkCode.Lifetime };
            db.ParentLinkCodes.Add(row);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ParentCodeDto(ParentLinkCode.Format(code), row.ExpiresAtUtc));
        });

        s.MapGet("/parents", async (ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var studentId = await db.StudentProfiles.Where(x => x.UserId == me.UserId).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (studentId is null)
            {
                return Results.NotFound();
            }
            var rows = await (
                from l in db.ParentStudentLinks.AsNoTracking()
                join u in db.Users on l.ParentUserId equals u.Id
                where l.StudentId == studentId && l.Status != ParentLinkStatus.Revoked
                orderby l.CreatedAtUtc
                select new { l.Id, l.Relationship, u.Email, l.Status, l.ConsentGrantedAtUtc }).ToListAsync(ct);
            return Results.Ok(rows.Select(r => new StudentParentDto(r.Id, r.Relationship, Privacy.MaskEmail(r.Email), r.Status.ToString(), r.ConsentGrantedAtUtc != null)));
        });

        // ---- Parent side. Any signed-in account may link (it becomes a parent by doing so).
        var p = v1.MapGroup("/parents").WithTags("Parents").RequireAuthorization();

        p.MapPost("/link", async (LinkRequest req, ICurrentUser me, IClock clock, GoogolsDbContext db, UserManager<AppUser> users, CancellationToken ct) =>
        {
            var relationship = req.Relationship?.Trim().ToLowerInvariant() ?? "";
            if (!Relationships.Contains(relationship))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["relationship"] = ["Choose mother, father, guardian or other."] });
            }
            if (string.IsNullOrWhiteSpace(req.Code) || ParentLinkCode.Normalize(req.Code).Length != ParentLinkCode.Length)
            {
                return InvalidCode();
            }
            if (me.UserId is not { } userId)
            {
                return Results.Unauthorized();
            }
            var now = clock.UtcNow;
            var hash = ParentLinkCode.Hash(req.Code);
            var code = await db.ParentLinkCodes.FirstOrDefaultAsync(c => c.CodeHash == hash, ct);
            if (code is null || !code.IsUsable(now))
            {
                return InvalidCode();
            }
            var student = await db.StudentProfiles.AsNoTracking().FirstAsync(x => x.Id == code.StudentId, ct);
            if (student.UserId == userId)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["A parent needs to sign in with their own account, not the student's."] });
            }

            var link = await db.ParentStudentLinks.FirstOrDefaultAsync(l => l.ParentUserId == userId && l.StudentId == student.Id, ct);
            if (link is null)
            {
                var active = await db.ParentStudentLinks.CountAsync(l => l.StudentId == student.Id && l.Status != ParentLinkStatus.Revoked, ct);
                if (active >= ParentalConsent.MaxParentsPerStudent)
                {
                    return Results.Conflict(new { message = "This student already has the maximum number of linked parents." });
                }
                link = new ParentStudentLink { ParentUserId = userId, StudentId = student.Id, Relationship = relationship };
                db.ParentStudentLinks.Add(link);
            }
            link.Relationship = relationship;
            link.Status = ParentLinkStatus.Verified; // possession of the student's one-time code verifies the link
            link.VerifiedAtUtc = now;
            code.UsedAtUtc = now;
            await db.SaveChangesAsync(ct);

            var user = await users.FindByIdAsync(userId.ToString());
            if (user is not null && !await users.IsInRoleAsync(user, Roles.Parent))
            {
                await users.AddToRoleAsync(user, Roles.Parent);
            }
            return Results.Ok(new { studentId = student.Id, displayName = student.DisplayName });
        }).RequireRateLimiting("auth");

        p.MapGet("/children", async (ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var today = clock.TodayIn("Asia/Kolkata");
            var links = await db.ParentStudentLinks.AsNoTracking()
                .Where(l => l.ParentUserId == me.UserId && l.Status == ParentLinkStatus.Verified)
                .Select(l => new { l.StudentId, l.ConsentGrantedAtUtc, l.ConsentVersion, l.Student }).ToListAsync(ct);
            var result = new List<ChildDto>();
            foreach (var l in links.Where(l => l.Student is not null))
            {
                var st = l.Student!;
                var enr = await (
                    from e in db.Enrollments.AsNoTracking()
                    join c in db.Curricula on e.CurriculumId equals c.Id
                    join b in db.CurriculumBoards on c.BoardId equals b.Id
                    join k in db.AcademicClasses on e.ClassId equals k.Id
                    where e.StudentId == st.Id && e.IsActive
                    select new { k.GradeName, b.Code, c.AcademicYear }).FirstOrDefaultAsync(ct);
                var recent = await db.XpTransactions.AsNoTracking().Where(x => x.StudentId == st.Id)
                    .OrderByDescending(x => x.CreatedAtUtc).Take(5)
                    .Select(x => new RecentXpDto(x.Points, x.Reason, x.CreatedAtUtc)).ToListAsync(ct);
                var granted = l.ConsentGrantedAtUtc != null && l.ConsentVersion == ParentalConsent.CurrentVersion;
                result.Add(new ChildDto(st.Id, st.DisplayName, enr?.GradeName, enr?.Code, enr?.AcademicYear,
                    XpAwardPolicy.LevelFor(st.TotalXp), st.TotalXp, st.CurrentStreakDays, st.LastActiveLocalDate,
                    st.RequiresParentalConsent(today), granted, l.ConsentVersion, l.ConsentGrantedAtUtc, recent));
            }
            return Results.Ok(result);
        });

        p.MapPost("/children/{studentId:guid}/consent", async (Guid studentId, ConsentRequest req, ICurrentUser me, IClock clock,
            GoogolsDbContext db, UserManager<AppUser> users, IEmailSender email, CancellationToken ct) =>
        {
            if (!req.Accept || req.ConsentVersion != ParentalConsent.CurrentVersion)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["consentVersion"] = ["Please read and accept the current consent notice."] });
            }
            var link = await db.ParentStudentLinks.Include(l => l.Student)
                .FirstOrDefaultAsync(l => l.ParentUserId == me.UserId && l.StudentId == studentId && l.Status == ParentLinkStatus.Verified, ct);
            if (link is null)
            {
                return Results.NotFound(); // not this parent's child: same answer as "doesn't exist"
            }
            link.ConsentGrantedAtUtc = clock.UtcNow;
            link.ConsentVersion = ParentalConsent.CurrentVersion;
            await db.SaveChangesAsync(ct);

            var parent = await users.FindByIdAsync(me.UserId!.Value.ToString());
            if (parent?.Email is { } to)
            {
                var name = HtmlEncoder.Default.Encode(link.Student?.DisplayName ?? "your child");
                await email.SendAsync(to, "You gave consent on Googols",
                    EmailTemplates.Wrap("Consent recorded",
                        $"<p>You gave consent (notice version {ParentalConsent.CurrentVersion}) for <strong>{name}</strong> to use Googols learning and AI features on {clock.UtcNow:dd MMM yyyy HH:mm} UTC.</p><p>You can withdraw consent at any time from your parent dashboard.</p>"),
                    ct);
            }
            return Results.NoContent();
        });

        p.MapDelete("/children/{studentId:guid}/consent", async (Guid studentId, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.ParentStudentLinks
                .Where(l => l.ParentUserId == me.UserId && l.StudentId == studentId && l.Status == ParentLinkStatus.Verified)
                .ExecuteUpdateAsync(u => u.SetProperty(l => l.ConsentGrantedAtUtc, (DateTime?)null).SetProperty(l => l.ConsentVersion, (string?)null), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        p.MapDelete("/children/{studentId:guid}", async (Guid studentId, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.ParentStudentLinks
                .Where(l => l.ParentUserId == me.UserId && l.StudentId == studentId && l.Status != ParentLinkStatus.Revoked)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(l => l.Status, ParentLinkStatus.Revoked)
                    .SetProperty(l => l.ConsentGrantedAtUtc, (DateTime?)null)
                    .SetProperty(l => l.ConsentVersion, (string?)null), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static IResult InvalidCode() =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["That code is wrong or has expired. Ask your child to create a new one."] });
}
