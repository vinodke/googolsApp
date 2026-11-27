using Googols.Application.Abstractions;
using Googols.Application.Common;
using Googols.Application.Gamification;
using Googols.Domain.Common;
using Googols.Domain.Schools;
using Googols.Domain.Students;
using Googols.Infrastructure.Identity;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

public sealed record SchoolRequest(string Name, string Code, string? City);
public sealed record EmailRequest(string Email);
public sealed record PersonDto(Guid UserId, string Email, string? Name, string Role);
public sealed record AdminSchoolDto(Guid Id, string Name, string Code, string? City, int Divisions, int Students, IReadOnlyList<PersonDto> Admins);
public sealed record ClassOption(Guid Id, string Label);
public sealed record SchoolOverviewDto(Guid Id, string Name, string Code, string? City, int Divisions, int Students, int Teachers, IReadOnlyList<ClassOption> Classes);
public sealed record DivisionRequest(Guid ClassId, string Name);
public sealed record DivisionUpdateRequest(string Name, bool JoiningOpen);
public sealed record DivisionDto(Guid Id, string Name, Guid ClassId, string ClassLabel, string JoinCode, bool JoiningOpen, int Students, int Allocations);
public sealed record DivisionStudentDto(Guid StudentId, string Name, int TotalXp, int Level, int StreakDays, DateOnly? LastActiveDate, int LessonsCompleted);
public sealed record AllocationDto(Guid Id, Guid SubjectId, string Subject, Guid TeacherUserId, string Teacher, Guid ClassroomId);
public sealed record SubjectOption(Guid Id, string Name);
public sealed record DivisionDetailDto(DivisionDto Division, IReadOnlyList<DivisionStudentDto> Students, IReadOnlyList<AllocationDto> Allocations,
    IReadOnlyList<SubjectOption> Subjects, IReadOnlyList<PersonDto> Teachers);
public sealed record AllocationRequest(Guid SubjectId, Guid TeacherUserId);
public sealed record SlotDto(int DayOfWeek, int Period, Guid? SubjectId, Guid? TeacherUserId, string? StartTime, string? EndTime, string? Label,
    string? Subject = null, string? Teacher = null, string? Division = null);
public sealed record TimetableRequest(IReadOnlyList<SlotDto> Slots);
public sealed record MyTimetableDto(Guid DivisionId, string Division, string School, IReadOnlyList<SlotDto> Slots);

/// <summary>Finds the school the signed-in user administers. Every /school route goes through this, so one school never sees another's data.</summary>
public sealed class SchoolAccess(GoogolsDbContext db, ICurrentUser me)
{
    public Task<School?> AdminSchoolAsync(Guid? schoolId, CancellationToken ct)
    {
        var q = from m in db.SchoolMembers
                join s in db.Schools on m.SchoolId equals s.Id
                where m.UserId == me.UserId && m.Role == SchoolRoles.Admin
                select s;
        if (schoolId is { } id)
        {
            q = q.Where(s => s.Id == id);
        }
        return q.OrderBy(s => s.Name).FirstOrDefaultAsync(ct);
    }

    public Task<Division?> DivisionAsync(School school, Guid divisionId, CancellationToken ct) =>
        db.Divisions.FirstOrDefaultAsync(d => d.Id == divisionId && d.SchoolId == school.Id, ct);
}

/// <summary>Joining and leaving school divisions; keeps the division's linked classrooms in step.</summary>
public static class DivisionMembership
{
    public static async Task JoinAsync(GoogolsDbContext db, IClock clock, Division division, StudentProfile student, CancellationToken ct)
    {
        // One division per school: joining 8B moves a student out of 8A.
        var others = await db.Divisions.Where(d => d.SchoolId == division.SchoolId && d.Id != division.Id).Select(d => d.Id).ToListAsync(ct);
        foreach (var other in others)
        {
            await LeaveAsync(db, other, student.Id, ct);
        }
        if (!await db.DivisionMembers.AnyAsync(m => m.DivisionId == division.Id && m.StudentId == student.Id, ct))
        {
            db.DivisionMembers.Add(new DivisionMember { DivisionId = division.Id, StudentId = student.Id, JoinedAtUtc = clock.UtcNow });
        }
        var rooms = await db.Classrooms.Where(c => c.DivisionId == division.Id).Select(c => c.Id).ToListAsync(ct);
        var already = await db.ClassroomMembers.Where(m => m.StudentId == student.Id && rooms.Contains(m.ClassroomId)).Select(m => m.ClassroomId).ToListAsync(ct);
        foreach (var room in rooms.Except(already))
        {
            db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = room, StudentId = student.Id, JoinedAtUtc = clock.UtcNow });
        }
        await db.SaveChangesAsync(ct);
        await db.StudentProfiles.Where(s => s.Id == student.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.SchoolId, division.SchoolId), ct);
    }

    public static async Task LeaveAsync(GoogolsDbContext db, Guid divisionId, Guid studentId, CancellationToken ct)
    {
        var rooms = db.Classrooms.IgnoreQueryFilters().Where(c => c.DivisionId == divisionId).Select(c => c.Id);
        await db.ClassroomMembers.Where(m => m.StudentId == studentId && rooms.Contains(m.ClassroomId)).ExecuteDeleteAsync(ct);
        var n = await db.DivisionMembers.Where(m => m.DivisionId == divisionId && m.StudentId == studentId).ExecuteDeleteAsync(ct);
        if (n > 0 && !await db.DivisionMembers.AnyAsync(m => m.StudentId == studentId, ct))
        {
            await db.StudentProfiles.Where(s => s.Id == studentId).ExecuteUpdateAsync(u => u.SetProperty(s => s.SchoolId, (Guid?)null), ct);
        }
    }

    /// <summary>A join code unused by any classroom or division (they share one "class code" box for students).</summary>
    public static async Task<string> UniqueCodeAsync(GoogolsDbContext db, CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var code = Codes.New(TeacherEndpoints.JoinCodeLength);
            if (!await db.Classrooms.IgnoreQueryFilters().AnyAsync(c => c.JoinCode == code, ct) && !await db.Divisions.IgnoreQueryFilters().AnyAsync(d => d.JoinCode == code, ct))
            {
                return code;
            }
        }
        throw new InvalidOperationException("Could not generate a unique code.");
    }
}

public static class SchoolEndpoints
{
    private static readonly string[] Days = ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public static void MapSchoolEndpoints(this RouteGroupBuilder v1)
    {
        MapPlatformAdmin(v1.MapGroup("/admin/schools").WithTags("Schools").RequireAuthorization(Policies.Admin));
        MapSchoolAdmin(v1.MapGroup("/school").WithTags("School admin").RequireAuthorization(Policies.SchoolAdmin));

        v1.MapGet("/learning/timetable", async (StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var divisions = await (from m in db.DivisionMembers join d in db.Divisions on m.DivisionId equals d.Id join s in db.Schools on d.SchoolId equals s.Id
                                   where m.StudentId == ctx.Student.Id select new { d.Id, d.Name, School = s.Name }).ToListAsync(ct);
            var result = new List<MyTimetableDto>();
            foreach (var d in divisions)
            {
                result.Add(new MyTimetableDto(d.Id, d.Name, d.School, await SlotsAsync(db, db.TimetableSlots.Where(t => t.DivisionId == d.Id), ct)));
            }
            return Results.Ok(result);
        }).RequireAuthorization(Policies.Student).WithTags("Timetable");

        v1.MapGet("/teacher/timetable", async (ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
            Results.Ok(await SlotsAsync(db, db.TimetableSlots.Where(t => t.TeacherUserId == me.UserId
                && db.Divisions.Any(d => d.Id == t.DivisionId)), ct)))
            .RequireAuthorization(Policies.Teacher).WithTags("Timetable");
    }

    private static async Task<List<SlotDto>> SlotsAsync(GoogolsDbContext db, IQueryable<TimetableSlot> q, CancellationToken ct)
    {
        var rows = await q.AsNoTracking().OrderBy(t => t.DayOfWeek).ThenBy(t => t.Period)
            .Select(t => new
            {
                t.DayOfWeek, t.Period, t.SubjectId, t.TeacherUserId, t.StartTime, t.EndTime, t.Label,
                Subject = db.Subjects.Where(s => s.Id == t.SubjectId).Select(s => s.Name).FirstOrDefault(),
                Teacher = db.Users.Where(u => u.Id == t.TeacherUserId).Select(u => u.FullName != "" ? u.FullName : u.Email).FirstOrDefault(),
                Division = db.Divisions.Where(d => d.Id == t.DivisionId).Select(d => d.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        return rows.Select(r => new SlotDto(r.DayOfWeek, r.Period, r.SubjectId, r.TeacherUserId, r.StartTime?.ToString("HH:mm"), r.EndTime?.ToString("HH:mm"),
            r.Label, r.Subject, r.Teacher, r.Division)).ToList();
    }

    private static void MapPlatformAdmin(RouteGroupBuilder g)
    {
        g.MapGet("", async (GoogolsDbContext db, CancellationToken ct) =>
        {
            var schools = await db.Schools.AsNoTracking().OrderBy(s => s.Name)
                .Select(s => new
                {
                    s.Id, s.Name, s.Code, s.City,
                    Divisions = db.Divisions.Count(d => d.SchoolId == s.Id),
                    Students = db.DivisionMembers.Where(m => db.Divisions.Any(d => d.Id == m.DivisionId && d.SchoolId == s.Id)).Select(m => m.StudentId).Distinct().Count(),
                }).ToListAsync(ct);
            var ids = schools.Select(s => s.Id).ToList();
            var admins = await (from m in db.SchoolMembers join u in db.Users on m.UserId equals u.Id
                                where ids.Contains(m.SchoolId) && m.Role == SchoolRoles.Admin
                                select new { m.SchoolId, u.Id, u.Email, u.FullName }).ToListAsync(ct);
            return schools.Select(s => new AdminSchoolDto(s.Id, s.Name, s.Code, s.City, s.Divisions, s.Students,
                admins.Where(a => a.SchoolId == s.Id).Select(a => new PersonDto(a.Id, a.Email ?? "", a.FullName, SchoolRoles.Admin)).ToList()));
        });

        g.MapPost("", async (SchoolRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            var code = Codes.Normalize(req.Code ?? "");
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 200)
            {
                errors["name"] = ["Enter the school's name."];
            }
            if (code.Length is < 3 or > 30)
            {
                errors["code"] = ["Use a short code of 3–30 letters or numbers, e.g. GHSSKTM."];
            }
            else if (await db.Schools.IgnoreQueryFilters().AnyAsync(s => s.Code == code, ct))
            {
                errors["code"] = ["That code is already used by another school."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }
            var school = new School { Name = req.Name.Trim(), Code = code, City = string.IsNullOrWhiteSpace(req.City) ? null : req.City.Trim() };
            db.Schools.Add(school);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/schools/{school.Id}", new { school.Id });
        });

        g.MapPost("/{id:guid}/admins", async (Guid id, EmailRequest req, GoogolsDbContext db, UserManager<AppUser> users, CancellationToken ct) =>
        {
            if (!await db.Schools.AnyAsync(s => s.Id == id, ct))
            {
                return Results.NotFound();
            }
            var user = await users.FindByEmailAsync((req.Email ?? "").Trim());
            if (user is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["No Googols account uses that email. Ask them to sign up first."] });
            }
            await AddMemberAsync(db, users, id, user, SchoolRoles.Admin, ct);
            return Results.NoContent();
        });

        g.MapDelete("/{id:guid}/admins/{userId:guid}", async (Guid id, Guid userId, GoogolsDbContext db, UserManager<AppUser> users, CancellationToken ct) =>
        {
            var n = await db.SchoolMembers.Where(m => m.SchoolId == id && m.UserId == userId && m.Role == SchoolRoles.Admin).ExecuteDeleteAsync(ct);
            if (n > 0 && !await db.SchoolMembers.AnyAsync(m => m.UserId == userId && m.Role == SchoolRoles.Admin, ct) && await users.FindByIdAsync(userId.ToString()) is { } user)
            {
                await users.RemoveFromRoleAsync(user, Roles.SchoolAdmin);
                await users.UpdateSecurityStampAsync(user);
            }
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static async Task AddMemberAsync(GoogolsDbContext db, UserManager<AppUser> users, Guid schoolId, AppUser user, string role, CancellationToken ct)
    {
        var member = await db.SchoolMembers.FirstOrDefaultAsync(m => m.SchoolId == schoolId && m.UserId == user.Id, ct);
        if (member is null)
        {
            db.SchoolMembers.Add(new SchoolMember { SchoolId = schoolId, UserId = user.Id, Role = role });
        }
        else if (role == SchoolRoles.Admin)
        {
            member.Role = SchoolRoles.Admin; // promote a teacher; never demote an admin by adding them as staff
        }
        await db.SaveChangesAsync(ct);
        var identityRole = role == SchoolRoles.Admin ? Roles.SchoolAdmin : Roles.Teacher;
        if (!await users.IsInRoleAsync(user, identityRole))
        {
            await users.AddToRoleAsync(user, identityRole);
            await users.UpdateSecurityStampAsync(user); // new role reaches their session within a minute
        }
    }

    private static string ClassLabel(string board, string year, string grade) => $"{board} · {year} · {grade}";

    private static void MapSchoolAdmin(RouteGroupBuilder g)
    {
        g.MapGet("", async (Guid? schoolId, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s)
            {
                return Results.NotFound();
            }
            var classes = await (from k in db.AcademicClasses join c in db.Curricula on k.CurriculumId equals c.Id join b in db.CurriculumBoards on c.BoardId equals b.Id
                                 where c.Status == PublicationStatus.Published
                                 orderby b.Code, c.AcademicYear descending, k.GradeNumber
                                 select new { k.Id, b.Code, c.AcademicYear, k.GradeName }).ToListAsync(ct);
            var divisionIds = db.Divisions.Where(d => d.SchoolId == s.Id).Select(d => d.Id);
            return Results.Ok(new SchoolOverviewDto(s.Id, s.Name, s.Code, s.City,
                await divisionIds.CountAsync(ct),
                await db.DivisionMembers.Where(m => divisionIds.Contains(m.DivisionId)).Select(m => m.StudentId).Distinct().CountAsync(ct),
                await db.SchoolMembers.CountAsync(m => m.SchoolId == s.Id, ct),
                classes.Select(k => new ClassOption(k.Id, ClassLabel(k.Code, k.AcademicYear, k.GradeName))).ToList()));
        });

        // ---- Staff
        g.MapGet("/staff", async (Guid? schoolId, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s)
            {
                return Results.NotFound();
            }
            return Results.Ok(await StaffAsync(db, s.Id, ct));
        });

        g.MapPost("/staff", async (Guid? schoolId, EmailRequest req, SchoolAccess access, GoogolsDbContext db, UserManager<AppUser> users, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s)
            {
                return Results.NotFound();
            }
            var user = await users.FindByEmailAsync((req.Email ?? "").Trim());
            if (user is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["No Googols account uses that email. Ask the teacher to sign up first, then add them."] });
            }
            await AddMemberAsync(db, users, s.Id, user, SchoolRoles.Teacher, ct);
            return Results.NoContent();
        });

        g.MapDelete("/staff/{userId:guid}", async (Guid userId, Guid? schoolId, SchoolAccess access, ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s)
            {
                return Results.NotFound();
            }
            if (userId == me.UserId)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You can't remove yourself. Ask Googols support to change school admins.");
            }
            var n = await db.SchoolMembers.Where(m => m.SchoolId == s.Id && m.UserId == userId).ExecuteDeleteAsync(ct);
            if (n == 0)
            {
                return Results.NotFound();
            }
            // Their school classes stop: classrooms archived, allocations and timetable entries cleared.
            var divisions = db.Divisions.IgnoreQueryFilters().Where(d => d.SchoolId == s.Id).Select(d => d.Id);
            await db.Classrooms.Where(c => c.TeacherUserId == userId && c.SchoolId == s.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivedAtUtc, clock.UtcNow), ct);
            await db.TeacherAllocations.Where(a => a.TeacherUserId == userId && divisions.Contains(a.DivisionId)).ExecuteDeleteAsync(ct);
            await db.TimetableSlots.Where(t => t.TeacherUserId == userId && divisions.Contains(t.DivisionId))
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.TeacherUserId, (Guid?)null), ct);
            return Results.NoContent();
        });

        // ---- Divisions
        g.MapGet("/divisions", async (Guid? schoolId, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s)
            {
                return Results.NotFound();
            }
            return Results.Ok(await DivisionsAsync(db, db.Divisions.Where(d => d.SchoolId == s.Id), ct));
        });

        g.MapPost("/divisions", async (Guid? schoolId, DivisionRequest req, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s)
            {
                return Results.NotFound();
            }
            var name = req.Name?.Trim() ?? "";
            var errors = new Dictionary<string, string[]>();
            if (name.Length is < 1 or > 50)
            {
                errors["name"] = ["Name the division, e.g. 8A."];
            }
            if (!await db.AcademicClasses.AnyAsync(k => k.Id == req.ClassId && k.Curriculum!.Status == PublicationStatus.Published, ct))
            {
                errors["classId"] = ["Choose a published class."];
            }
            if (await db.Divisions.AnyAsync(d => d.SchoolId == s.Id && d.ClassId == req.ClassId && d.Name == name, ct))
            {
                errors["name"] = ["This school already has a division with that name for this class."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }
            var d = new Division { SchoolId = s.Id, ClassId = req.ClassId, Name = name, JoinCode = await DivisionMembership.UniqueCodeAsync(db, ct) };
            db.Divisions.Add(d);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/school/divisions/{d.Id}", new { d.Id, d.JoinCode });
        });

        g.MapGet("/divisions/{id:guid}", async (Guid id, Guid? schoolId, SchoolAccess access, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            var today = clock.TodayIn("Asia/Kolkata");
            var dto = (await DivisionsAsync(db, db.Divisions.Where(x => x.Id == d.Id), ct)).First();
            var students = await (from m in db.DivisionMembers join st in db.StudentProfiles on m.StudentId equals st.Id
                                  where m.DivisionId == d.Id orderby st.DisplayName
                                  select new { st.Id, st.DisplayName, st.TotalXp, st.CurrentStreakDays, st.LastActiveLocalDate,
                                      Lessons = db.LessonProgress.Count(p => p.StudentId == st.Id) }).ToListAsync(ct);
            var allocations = await (from a in db.TeacherAllocations join sub in db.Subjects on a.SubjectId equals sub.Id join u in db.Users on a.TeacherUserId equals u.Id
                                     where a.DivisionId == d.Id orderby sub.Sequence
                                     select new AllocationDto(a.Id, a.SubjectId, sub.Name, a.TeacherUserId, u.FullName != "" ? u.FullName : (u.Email ?? ""), a.ClassroomId)).ToListAsync(ct);
            var subjects = await db.Subjects.AsNoTracking().Where(x => x.ClassId == d.ClassId).OrderBy(x => x.Sequence).Select(x => new SubjectOption(x.Id, x.Name)).ToListAsync(ct);
            return Results.Ok(new DivisionDetailDto(dto,
                students.Select(x => new DivisionStudentDto(x.Id, x.DisplayName, x.TotalXp, XpAwardPolicy.LevelFor(x.TotalXp),
                    StreakPolicy.Current(x.CurrentStreakDays, x.LastActiveLocalDate, today), x.LastActiveLocalDate, x.Lessons)).ToList(),
                allocations, subjects, await StaffAsync(db, s.Id, ct)));
        });

        g.MapPut("/divisions/{id:guid}", async (Guid id, Guid? schoolId, DivisionUpdateRequest req, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            var name = req.Name?.Trim() ?? "";
            if (name.Length is < 1 or > 50)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name the division, e.g. 8A."] });
            }
            d.Name = name;
            d.JoiningOpen = req.JoiningOpen;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("/divisions/{id:guid}/rotate-code", async (Guid id, Guid? schoolId, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            d.JoinCode = await DivisionMembership.UniqueCodeAsync(db, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { d.JoinCode });
        });

        g.MapDelete("/divisions/{id:guid}", async (Guid id, Guid? schoolId, SchoolAccess access, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            d.ArchivedAtUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            await db.Classrooms.Where(c => c.DivisionId == d.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivedAtUtc, clock.UtcNow), ct);
            return Results.NoContent();
        });

        g.MapDelete("/divisions/{id:guid}/students/{studentId:guid}", async (Guid id, Guid studentId, Guid? schoolId, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            await DivisionMembership.LeaveAsync(db, d.Id, studentId, ct);
            return Results.NoContent();
        });

        g.MapGet("/divisions/{id:guid}/heatmap", async (Guid id, Guid? schoolId, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            return Results.Ok(await Heatmap.BuildAsync(db, d.ClassId, db.DivisionMembers.Where(m => m.DivisionId == d.Id).Select(m => m.StudentId), null, ct));
        });

        // ---- Teacher allocations: each one is a classroom owned by the teacher, kept in step with the division.
        g.MapPost("/divisions/{id:guid}/allocations", async (Guid id, Guid? schoolId, AllocationRequest req, SchoolAccess access, IClock clock,
            GoogolsDbContext db, UserManager<AppUser> users, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            var subject = await db.Subjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.SubjectId && x.ClassId == d.ClassId, ct);
            if (subject is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["subjectId"] = ["Choose a subject of this class."] });
            }
            if (!await db.SchoolMembers.AnyAsync(m => m.SchoolId == s.Id && m.UserId == req.TeacherUserId, ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["teacherUserId"] = ["Add this teacher to the school's staff first."] });
            }
            if (await users.FindByIdAsync(req.TeacherUserId.ToString()) is { } teacher && !await users.IsInRoleAsync(teacher, Roles.Teacher))
            {
                await users.AddToRoleAsync(teacher, Roles.Teacher);
                await users.UpdateSecurityStampAsync(teacher);
            }
            // Replacing the subject's teacher archives the old classroom (its history stays with the old teacher).
            var existing = await db.TeacherAllocations.FirstOrDefaultAsync(a => a.DivisionId == d.Id && a.SubjectId == subject.Id, ct);
            if (existing is not null)
            {
                await db.Classrooms.Where(c => c.Id == existing.ClassroomId).ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivedAtUtc, clock.UtcNow), ct);
                db.TeacherAllocations.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
            var room = new Classroom
            {
                TeacherUserId = req.TeacherUserId,
                ClassId = d.ClassId,
                Name = $"{d.Name} {subject.Name}",
                SchoolName = s.Name,
                SchoolId = s.Id,
                DivisionId = d.Id,
                SubjectId = subject.Id,
                JoinCode = await DivisionMembership.UniqueCodeAsync(db, ct),
                JoiningOpen = false, // students join through the division code
            };
            db.Classrooms.Add(room);
            var members = await db.DivisionMembers.Where(m => m.DivisionId == d.Id).Select(m => m.StudentId).ToListAsync(ct);
            foreach (var st in members)
            {
                db.ClassroomMembers.Add(new ClassroomMember { ClassroomId = room.Id, StudentId = st, JoinedAtUtc = clock.UtcNow });
            }
            var allocation = new TeacherAllocation { DivisionId = d.Id, SubjectId = subject.Id, TeacherUserId = req.TeacherUserId, ClassroomId = room.Id };
            db.TeacherAllocations.Add(allocation);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/school/divisions/{d.Id}/allocations/{allocation.Id}", new { allocation.Id, ClassroomId = room.Id });
        });

        g.MapDelete("/divisions/{id:guid}/allocations/{allocationId:guid}", async (Guid id, Guid allocationId, Guid? schoolId, SchoolAccess access, IClock clock,
            GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            var a = await db.TeacherAllocations.FirstOrDefaultAsync(x => x.Id == allocationId && x.DivisionId == d.Id, ct);
            if (a is null)
            {
                return Results.NotFound();
            }
            await db.Classrooms.Where(c => c.Id == a.ClassroomId).ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivedAtUtc, clock.UtcNow), ct);
            db.TeacherAllocations.Remove(a);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- Timetable: the whole week is saved at once.
        g.MapGet("/divisions/{id:guid}/timetable", async (Guid id, Guid? schoolId, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            return Results.Ok(await SlotsAsync(db, db.TimetableSlots.Where(t => t.DivisionId == d.Id), ct));
        });

        g.MapPut("/divisions/{id:guid}/timetable", async (Guid id, Guid? schoolId, TimetableRequest req, SchoolAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.AdminSchoolAsync(schoolId, ct) is not { } s || await access.DivisionAsync(s, id, ct) is not { } d)
            {
                return Results.NotFound();
            }
            var slots = req.Slots ?? [];
            var subjects = (await db.Subjects.Where(x => x.ClassId == d.ClassId).Select(x => x.Id).ToListAsync(ct)).ToHashSet();
            var staff = (await db.SchoolMembers.Where(m => m.SchoolId == s.Id).Select(m => m.UserId).ToListAsync(ct)).ToHashSet();
            var allocated = await db.TeacherAllocations.Where(a => a.DivisionId == d.Id).ToDictionaryAsync(a => a.SubjectId, a => a.TeacherUserId, ct);
            var errors = new List<string>();
            var rows = new List<TimetableSlot>();
            foreach (var x in slots)
            {
                var at = $"{(x.DayOfWeek is >= 1 and <= 6 ? Days[x.DayOfWeek] : "?")} period {x.Period}";
                if (x.DayOfWeek is < 1 or > 6 || x.Period is < 1 or > TimetableSlot.MaxPeriods)
                {
                    errors.Add($"{at}: days are Monday–Saturday and periods 1–{TimetableSlot.MaxPeriods}.");
                    continue;
                }
                if (x.SubjectId is { } sid && !subjects.Contains(sid))
                {
                    errors.Add($"{at}: that subject isn't taught in this class.");
                }
                if (x.TeacherUserId is { } tid && !staff.Contains(tid))
                {
                    errors.Add($"{at}: that teacher isn't on the school's staff.");
                }
                if (x.SubjectId is null && string.IsNullOrWhiteSpace(x.Label))
                {
                    continue; // empty period
                }
                TimeOnly? start = TimeOnly.TryParse(x.StartTime, out var st) ? st : null;
                TimeOnly? end = TimeOnly.TryParse(x.EndTime, out var en) ? en : null;
                if (start is not null && end is not null && end <= start)
                {
                    errors.Add($"{at}: the end time must be after the start time.");
                }
                rows.Add(new TimetableSlot
                {
                    DivisionId = d.Id, DayOfWeek = x.DayOfWeek, Period = x.Period, SubjectId = x.SubjectId,
                    TeacherUserId = x.TeacherUserId ?? (x.SubjectId is { } s2 && allocated.TryGetValue(s2, out var t2) ? t2 : null),
                    StartTime = start, EndTime = end, Label = string.IsNullOrWhiteSpace(x.Label) ? null : x.Label.Trim()[..Math.Min(40, x.Label.Trim().Length)],
                });
            }
            if (rows.GroupBy(r => (r.DayOfWeek, r.Period)).Any(grp => grp.Count() > 1))
            {
                errors.Add("Each period can only have one entry.");
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["slots"] = errors.Distinct().Take(10).ToArray() });
            }
            await db.TimetableSlots.Where(t => t.DivisionId == d.Id).ExecuteDeleteAsync(ct);
            db.TimetableSlots.AddRange(rows);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<List<PersonDto>> StaffAsync(GoogolsDbContext db, Guid schoolId, CancellationToken ct) =>
        await (from m in db.SchoolMembers join u in db.Users on m.UserId equals u.Id
               where m.SchoolId == schoolId orderby m.Role, u.Email
               select new PersonDto(u.Id, u.Email ?? "", u.FullName, m.Role)).ToListAsync(ct);

    private static async Task<List<DivisionDto>> DivisionsAsync(GoogolsDbContext db, IQueryable<Division> q, CancellationToken ct)
    {
        var rows = await (from d in q.AsNoTracking()
                          join k in db.AcademicClasses on d.ClassId equals k.Id
                          join c in db.Curricula on k.CurriculumId equals c.Id
                          join b in db.CurriculumBoards on c.BoardId equals b.Id
                          orderby k.GradeNumber, d.Name
                          select new
                          {
                              d.Id, d.Name, d.ClassId, b.Code, c.AcademicYear, k.GradeName, d.JoinCode, d.JoiningOpen,
                              Students = db.DivisionMembers.Count(m => m.DivisionId == d.Id),
                              Allocations = db.TeacherAllocations.Count(a => a.DivisionId == d.Id),
                          }).ToListAsync(ct);
        return rows.Select(r => new DivisionDto(r.Id, r.Name, r.ClassId, ClassLabel(r.Code, r.AcademicYear, r.GradeName), r.JoinCode, r.JoiningOpen, r.Students, r.Allocations)).ToList();
    }
}
