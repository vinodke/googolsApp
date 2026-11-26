# Database Design

SQL Server, EF Core 10 code-first with migrations. `GoogolsDbContext` (src/Googols.Infrastructure/Persistence) is the single source of truth for the schema.

## Engineering rules (applied in code)
| Rule | How |
|---|---|
| GUID identifiers | `Entity.Id = Guid.CreateVersion7()` (sequential, avoids index fragmentation) |
| UTC timestamps + audit | `CreatedAtUtc`, `UpdatedAtUtc`, `CreatedByUserId`, `UpdatedByUserId` set in `SaveChangesAsync` |
| Soft delete only where useful | `ISoftDeletable.ArchivedAtUtc` + global query filter (boards, curricula) |
| Concurrency | `IConcurrencyTracked.RowVersion` → SQL `rowversion` (student profiles) |
| School scoping | `ISchoolScoped.SchoolId`, indexed; filters applied per query |
| Large files | Blob Storage; SQL holds only the blob name |
| Integrity | FKs with `Restrict` by default; `Cascade` only for student-owned rows; unique and check constraints |
| Idempotency | XP ledger unique on `(StudentId, IdempotencyKey)` |

## Implemented tables (Phase 3 foundation)
| Schema.Table | Key columns | Constraints / indexes |
|---|---|---|
| identity.Users, Roles, UserRoles, UserClaims, UserLogins, UserTokens, RoleClaims | ASP.NET Core Identity, GUID keys; `Users.FullName`, `Status`, `PreferredLanguage` | Identity defaults |
| curriculum.CurriculumBoards | Name, Code, CountryCode, IsEntranceExam, ArchivedAtUtc | unique Code |
| curriculum.Curricula | BoardId, Name, AcademicYear, Version, Status | unique (BoardId, AcademicYear, Version) |
| curriculum.AcademicClasses | CurriculumId, GradeNumber, GradeName | unique (CurriculumId, GradeNumber); CHECK 0–12 |
| curriculum.Subjects | ClassId, Name, Code, MediumLanguage, Sequence | unique (ClassId, Code, MediumLanguage) |
| curriculum.Chapters | SubjectId, Title, Sequence, Status | (SubjectId, Sequence) |
| curriculum.Topics | ChapterId, Title, Sequence | (ChapterId, Sequence) |
| curriculum.LearningObjectives | TopicId, Description | FK |
| learning.StudentProfiles | UserId, DisplayName, DateOfBirth, PreferredLanguage, SchoolId, TotalXp, CurrentStreakDays, LastActiveLocalDate, RowVersion | unique UserId; SchoolId index |
| learning.Enrollments | StudentId, CurriculumId, ClassId, AcademicYear, IsActive | (StudentId, IsActive), CurriculumId |
| learning.ParentStudentLinks | ParentUserId, StudentId, Relationship, Status, VerifiedAtUtc, ConsentGrantedAtUtc, ConsentVersion | unique (ParentUserId, StudentId) |
| gamification.XpTransactions | StudentId, Points, Reason, IdempotencyKey, RuleVersion | **unique (StudentId, IdempotencyKey)** |
| gamification.XpRules | ActivityCode, Points, IsActive | unique ActivityCode; seeded with blueprint defaults |
| jobs.* | Hangfire tables | created by Hangfire |

`StudentProfiles.TotalXp` is a cache, always recomputed from the ledger sum after an award.

## Planned tables by phase
| Phase | Tables |
|---|---|
| 5 Curriculum & content | LearningContent, ContentSources (licence, source URL, review status), ContentReviews, Bookmarks |
| 6 Student portal | LearningSessions, LearningProgress (mastery score), LessonAttempts, StudyPlans, StudyPlanTasks |
| 7 AI Teacher | AIConversations, AIMessages (retention-limited), AIUsageLogs (tokens, cost), AIContentReferences, AIModelConfigurations, AIInteractionFeedback, AIProfiles |
| 8 Exams & analytics | Questions (bank, not owned by an assessment), QuestionOptions, QuestionAnswerKeys, QuestionReviews, Assessments, AssessmentQuestions (join), ExamAttempts, StudentAnswers, AssessmentResults |
| 9 Gamification | StudentLevels (or computed), Badges, StudentBadges, Challenges, ChallengeProgress, LeaderboardSnapshots |
| 10 Schools/parents/teachers | Schools, SchoolMemberships, AcademicYears, Classrooms (+Division), ClassroomStudents, ClassroomTeachers, Timetables, Assignments, AssignmentSubmissions, Notifications |
| 11 Commerce/admin | SubscriptionPlans, Subscriptions, Payments, Invoices, Coupons, FeatureEntitlements, NotificationTemplates, AuditLogs, TenantConfigurations, SystemSettings |

Change from the blueprint: `Questions` sit in a bank and link to assessments through `AssessmentQuestions`, so one question can appear in many tests (the blueprint's `Questions.AssessmentId` would force duplication).

## Migrations workflow
```powershell
dotnet ef migrations add <Name> -p src/Googols.Infrastructure -s src/Googols.WebApi -o Persistence/Migrations
dotnet ef migrations script --idempotent -o deploy.sql   # review before production
```
Production: apply the idempotent script in the release pipeline, never `Database.Migrate()` at app start.

## Data retention (to confirm with legal review)
- AI messages: 90 days, then deleted. Usage logs keep counts only.
- Child account deletion erases learning rows and anonymises ledger rows.
- Backups: Azure SQL point-in-time restore, 7–35 days.
