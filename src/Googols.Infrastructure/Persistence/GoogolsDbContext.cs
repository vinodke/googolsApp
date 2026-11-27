using Googols.Domain.Common;
using Googols.Domain.Curricula;
using Googols.Domain.Gamification;
using Googols.Domain.Students;
using Googols.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Googols.Infrastructure.Persistence;

public class GoogolsDbContext(DbContextOptions<GoogolsDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<CurriculumBoard> CurriculumBoards => Set<CurriculumBoard>();
    public DbSet<Curriculum> Curricula => Set<Curriculum>();
    public DbSet<AcademicClass> AcademicClasses => Set<AcademicClass>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<LearningObjective> LearningObjectives => Set<LearningObjective>();

    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<ParentStudentLink> ParentStudentLinks => Set<ParentStudentLink>();
    public DbSet<ParentLinkCode> ParentLinkCodes => Set<ParentLinkCode>();

    public DbSet<XpTransaction> XpTransactions => Set<XpTransaction>();
    public DbSet<XpRule> XpRules => Set<XpRule>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("dbo");

        // Identity tables keep their names but live under an "identity" schema for clarity.
        b.Entity<AppUser>().ToTable("Users", "identity");
        b.Entity<IdentityRole<Guid>>().ToTable("Roles", "identity");
        b.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", "identity");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "identity");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "identity");
        b.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "identity");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "identity");
        b.Entity<AppUser>().Property(u => u.FullName).HasMaxLength(200);
        b.Entity<AppUser>().Property(u => u.PreferredLanguage).HasMaxLength(8);

        // Curriculum
        b.Entity<CurriculumBoard>(e =>
        {
            e.ToTable("CurriculumBoards", "curriculum");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasQueryFilter(x => x.ArchivedAtUtc == null);
        });
        b.Entity<Curriculum>(e =>
        {
            e.ToTable("Curricula", "curriculum");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.AcademicYear).HasMaxLength(9);
            e.HasIndex(x => new { x.BoardId, x.AcademicYear, x.Version }).IsUnique();
            e.HasOne(x => x.Board).WithMany(x => x.Curricula).HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.ArchivedAtUtc == null);
        });
        b.Entity<AcademicClass>(e =>
        {
            e.ToTable("AcademicClasses", "curriculum", t => t.HasCheckConstraint("CK_AcademicClasses_Grade", "[GradeNumber] BETWEEN 0 AND 12"));
            e.Property(x => x.GradeName).HasMaxLength(50);
            e.HasIndex(x => new { x.CurriculumId, x.GradeNumber }).IsUnique();
            e.HasOne(x => x.Curriculum).WithMany(x => x.Classes).HasForeignKey(x => x.CurriculumId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Subject>(e =>
        {
            e.ToTable("Subjects", "curriculum");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.MediumLanguage).HasMaxLength(8);
            e.HasIndex(x => new { x.ClassId, x.Code, x.MediumLanguage }).IsUnique();
            e.HasOne(x => x.Class).WithMany(x => x.Subjects).HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Chapter>(e =>
        {
            e.ToTable("Chapters", "curriculum");
            e.Property(x => x.Title).HasMaxLength(300);
            e.HasIndex(x => new { x.SubjectId, x.Sequence });
            e.HasOne(x => x.Subject).WithMany(x => x.Chapters).HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Topic>(e =>
        {
            e.ToTable("Topics", "curriculum");
            e.Property(x => x.Title).HasMaxLength(300);
            e.HasIndex(x => new { x.ChapterId, x.Sequence });
            e.HasOne(x => x.Chapter).WithMany(x => x.Topics).HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LearningObjective>(e =>
        {
            e.ToTable("LearningObjectives", "curriculum");
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasOne(x => x.Topic).WithMany(x => x.Objectives).HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Restrict);
        });

        // Students
        b.Entity<StudentProfile>(e =>
        {
            e.ToTable("StudentProfiles", "learning");
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.PreferredLanguage).HasMaxLength(8);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.SchoolId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Enrollment>(e =>
        {
            e.ToTable("Enrollments", "learning");
            e.Property(x => x.AcademicYear).HasMaxLength(9);
            e.HasIndex(x => new { x.StudentId, x.IsActive });
            e.HasIndex(x => x.CurriculumId);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Curriculum>().WithMany().HasForeignKey(x => x.CurriculumId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademicClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ParentStudentLink>(e =>
        {
            e.ToTable("ParentStudentLinks", "learning");
            e.Property(x => x.Relationship).HasMaxLength(40);
            e.Property(x => x.ConsentVersion).HasMaxLength(20);
            e.HasIndex(x => new { x.ParentUserId, x.StudentId }).IsUnique();
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ParentUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ParentLinkCode>(e =>
        {
            e.ToTable("ParentLinkCodes", "learning");
            e.Property(x => x.CodeHash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.HasIndex(x => x.StudentId);
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        // Gamification
        b.Entity<XpTransaction>(e =>
        {
            e.ToTable("XpTransactions", "gamification");
            e.Property(x => x.Reason).HasMaxLength(100);
            e.Property(x => x.IdempotencyKey).HasMaxLength(200);
            e.Property(x => x.RuleVersion).HasMaxLength(20);
            e.HasIndex(x => new { x.StudentId, x.IdempotencyKey }).IsUnique();
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<XpRule>(e =>
        {
            e.ToTable("XpRules", "gamification");
            e.Property(x => x.ActivityCode).HasMaxLength(60);
            e.HasIndex(x => x.ActivityCode).IsUnique();
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
