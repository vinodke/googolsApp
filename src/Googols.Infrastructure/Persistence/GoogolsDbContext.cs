using Googols.Domain.Ai;
using Googols.Domain.Assessment;
using Googols.Domain.Billing;
using Googols.Domain.Common;
using Googols.Domain.Content;
using Googols.Domain.Curricula;
using Googols.Domain.Gamification;
using Googols.Domain.Schools;
using Googols.Domain.Site;
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

    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<ContentChunk> ContentChunks => Set<ContentChunk>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();

    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    public DbSet<AiEvalCase> AiEvalCases => Set<AiEvalCase>();
    public DbSet<AiEvalRun> AiEvalRuns => Set<AiEvalRun>();

    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<WrittenQuestion> WrittenQuestions => Set<WrittenQuestion>();
    public DbSet<WrittenAnswer> WrittenAnswers => Set<WrittenAnswer>();
    public DbSet<ChapterMastery> ChapterMastery => Set<ChapterMastery>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<ClassroomMember> ClassroomMembers => Set<ClassroomMember>();

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    public DbSet<XpTransaction> XpTransactions => Set<XpTransaction>();
    public DbSet<XpRule> XpRules => Set<XpRule>();
    public DbSet<StudentBadge> StudentBadges => Set<StudentBadge>();

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
            e.Property(x => x.ShowOnLeaderboard).HasDefaultValue(true).HasSentinel(true); // existing rows get true; an explicit false is still written
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

        // Content (Phase 5) and progress (Phase 6)
        b.Entity<Lesson>(e =>
        {
            e.ToTable("Lessons", "content");
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Language).HasMaxLength(8);
            e.Property(x => x.AttachmentBlobName).HasMaxLength(200);
            e.Property(x => x.AttachmentFileName).HasMaxLength(260);
            e.Property(x => x.AttachmentContentType).HasMaxLength(100);
            e.HasIndex(x => new { x.ChapterId, x.Sequence });
            e.HasOne(x => x.Chapter).WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.ArchivedAtUtc == null);
        });
        b.Entity<ContentChunk>(e =>
        {
            e.ToTable("ContentChunks", "content");
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasIndex(x => new { x.SubjectId, x.ChapterId, x.Sequence });
            e.HasIndex(x => x.LessonId);
            e.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<LessonProgress>(e =>
        {
            e.ToTable("LessonProgress", "learning");
            e.HasIndex(x => new { x.StudentId, x.LessonId }).IsUnique();
            e.HasIndex(x => new { x.StudentId, x.SubjectId });
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
        });

        // AI Teacher history (Phase 7)
        b.Entity<AiConversation>(e =>
        {
            e.ToTable("AiConversations", "ai");
            e.Property(x => x.Title).HasMaxLength(100);
            e.Property(x => x.Language).HasMaxLength(8);
            e.HasIndex(x => new { x.StudentId, x.LastMessageAtUtc });
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.ArchivedAtUtc == null);
        });
        b.Entity<AiMessage>(e =>
        {
            e.ToTable("AiMessages", "ai");
            e.Property(x => x.Role).HasMaxLength(12);
            e.Property(x => x.Mode).HasMaxLength(30);
            e.Property(x => x.SafetyCategory).HasMaxLength(30);
            e.Property(x => x.Model).HasMaxLength(60);
            e.Property(x => x.ImageBlobName).HasMaxLength(200);
            e.Property(x => x.FeedbackComment).HasMaxLength(500);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.FeedbackHelpful, x.FeedbackAtUtc });
            e.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AiUsageLog>(e =>
        {
            e.ToTable("UsageLogs", "ai");
            e.Property(x => x.Feature).HasMaxLength(30);
            e.Property(x => x.Model).HasMaxLength(60);
            e.Property(x => x.CostUsd).HasPrecision(12, 6);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => new { x.Feature, x.CreatedAtUtc });
        });
        b.Entity<AiEvalCase>(e =>
        {
            e.ToTable("EvalCases", "ai");
            e.Property(x => x.Question).HasMaxLength(1000);
            e.Property(x => x.Language).HasMaxLength(8);
            e.Property(x => x.Mode).HasMaxLength(30);
            e.Property(x => x.ExpectedKeywords).HasMaxLength(2000);
            e.Property(x => x.ForbiddenPhrases).HasMaxLength(1000);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.ArchivedAtUtc == null);
        });
        b.Entity<AiEvalRun>(e =>
        {
            e.ToTable("EvalRuns", "ai");
            e.Property(x => x.PromptVersion).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(60);
            e.HasIndex(x => x.CreatedAtUtc);
        });

        // Question bank and quizzes (Phase 8)
        b.Entity<Question>(e =>
        {
            e.ToTable("Questions", "assessment", t => t.HasCheckConstraint("CK_Questions_Difficulty", "[Difficulty] BETWEEN 1 AND 3"));
            e.Property(x => x.Prompt).HasMaxLength(1000);
            e.Property(x => x.OptionsJson).HasMaxLength(2500);
            e.Property(x => x.Explanation).HasMaxLength(1000);
            e.Property(x => x.Language).HasMaxLength(8);
            e.HasIndex(x => new { x.ChapterId, x.Status });
            e.HasOne<Chapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.ArchivedAtUtc == null);
        });
        b.Entity<QuizAttempt>(e =>
        {
            e.ToTable("QuizAttempts", "assessment");
            e.Property(x => x.QuestionIdsJson).HasMaxLength(1000);
            e.Property(x => x.AnswersJson).HasMaxLength(200);
            e.HasIndex(x => new { x.StudentId, x.ChapterId, x.SubmittedAtUtc });
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Chapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Restrict);
        });

        // Timed tests, written answers, mastery and bookmarks
        b.Entity<Exam>(e =>
        {
            e.ToTable("Exams", "assessment");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.ChapterIdsJson).HasMaxLength(2000);
            e.HasIndex(x => new { x.ClassId, x.Kind, x.Status });
            e.HasIndex(x => x.ClassroomId);
            e.HasIndex(x => x.CreatedForStudentId);
            e.HasOne<AcademicClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.ArchivedAtUtc == null);
        });
        b.Entity<ExamAttempt>(e =>
        {
            e.ToTable("ExamAttempts", "assessment");
            e.Property(x => x.QuestionIdsJson).HasMaxLength(2500);
            e.Property(x => x.WrittenIdsJson).HasMaxLength(500);
            e.Property(x => x.AnswersJson).HasMaxLength(600);
            e.HasIndex(x => new { x.StudentId, x.ExamId });
            e.HasIndex(x => new { x.ExamId, x.SubmittedAtUtc });
            e.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WrittenQuestion>(e =>
        {
            e.ToTable("WrittenQuestions", "assessment");
            e.Property(x => x.Prompt).HasMaxLength(1000);
            e.Property(x => x.ModelAnswer).HasMaxLength(3000);
            e.Property(x => x.RubricJson).HasMaxLength(4000);
            e.Property(x => x.Language).HasMaxLength(8);
            e.HasIndex(x => new { x.ChapterId, x.Status });
            e.HasOne<Chapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.ArchivedAtUtc == null);
        });
        b.Entity<WrittenAnswer>(e =>
        {
            e.ToTable("WrittenAnswers", "assessment");
            e.Property(x => x.AnswerText).HasMaxLength(3000);
            e.Property(x => x.AiFeedbackJson).HasMaxLength(4000);
            e.Property(x => x.TeacherComment).HasMaxLength(1000);
            e.Ignore(x => x.FinalScore);
            e.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
            e.HasIndex(x => new { x.StudentId, x.Status });
            e.HasOne<ExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<WrittenQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ChapterMastery>(e =>
        {
            e.ToTable("ChapterMastery", "learning");
            e.HasIndex(x => new { x.StudentId, x.ChapterId }).IsUnique();
            e.HasIndex(x => x.ChapterId);
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Chapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Bookmark>(e =>
        {
            e.ToTable("Bookmarks", "learning");
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Snippet).HasMaxLength(500);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.StudentId, x.Kind, x.RefId }).IsUnique();
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        // Teachers (Phase 10)
        b.Entity<Classroom>(e =>
        {
            e.ToTable("Classrooms", "school");
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.SchoolName).HasMaxLength(200);
            e.Property(x => x.JoinCode).HasMaxLength(8);
            e.HasIndex(x => x.JoinCode).IsUnique();
            e.HasIndex(x => x.TeacherUserId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademicClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.ArchivedAtUtc == null);
        });
        b.Entity<ClassroomMember>(e =>
        {
            e.ToTable("ClassroomMembers", "school");
            e.HasIndex(x => new { x.ClassroomId, x.StudentId }).IsUnique();
            e.HasIndex(x => x.StudentId);
            e.HasOne<Classroom>().WithMany().HasForeignKey(x => x.ClassroomId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        // Billing (Phase 11)
        b.Entity<Payment>(e =>
        {
            e.ToTable("Payments", "billing");
            e.Property(x => x.PlanCode).HasMaxLength(40);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Provider).HasMaxLength(20);
            e.Property(x => x.ProviderOrderId).HasMaxLength(60);
            e.Property(x => x.ProviderPaymentId).HasMaxLength(60);
            e.HasIndex(x => x.ProviderOrderId).IsUnique();
            e.HasIndex(x => x.StudentId);
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Subscription>(e =>
        {
            e.ToTable("Subscriptions", "billing");
            e.Property(x => x.PlanCode).HasMaxLength(40);
            e.HasIndex(x => x.StudentId).IsUnique();
            e.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        // Public website (contact form)
        b.Entity<ContactMessage>(e =>
        {
            e.ToTable("ContactMessages", "site");
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Topic).HasMaxLength(20);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.HasIndex(x => new { x.Handled, x.CreatedAtUtc });
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
        b.Entity<StudentBadge>(e =>
        {
            e.ToTable("StudentBadges", "gamification");
            e.Property(x => x.BadgeCode).HasMaxLength(40);
            e.HasIndex(x => new { x.StudentId, x.BadgeCode }).IsUnique();
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
            if (entry.State == EntityState.Added && entry.Entity.CreatedAtUtc == default)
            {
                entry.Entity.CreatedAtUtc = now; // callers may set it explicitly to fix ordering (e.g. chat turns)
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
