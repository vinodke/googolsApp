// Mirrors the API DTOs (src/Googols.Application/Curriculum/CurriculumDtos.cs and Features/*.cs).
// Regenerate from /openapi/v1.json once the contract settles.

export type Lang = 'en' | 'ml'

export interface Board { id: string; name: string; code: string; countryCode: string; isEntranceExam: boolean }
export interface CurriculumSummary { id: string; name: string; academicYear: string; version: number }
export interface AcademicClass { id: string; gradeNumber: number; gradeName: string }
export interface Subject { id: string; name: string; code: string; mediumLanguage: string }
export interface Chapter { id: string; title: string; sequence: number }

export interface Enrollment { curriculumId: string; curriculumName: string; academicYear: string; classId: string; className: string; boardCode: string }
export interface StudentMe {
  id: string
  displayName: string
  preferredLanguage: Lang
  totalXp: number
  level: number
  streakDays: number
  enrollment: Enrollment | null
  consentRequired: boolean
}

export interface XpSummary { total: number; level: number; recent: { points: number; reason: string; createdAtUtc: string }[] }

export type TeacherMode = 'PersonalTeacher' | 'DoubtSolver' | 'ExamCoach' | 'RevisionAssistant' | 'PracticeTeacher'
export interface ChatTurn { role: 'user' | 'assistant'; text: string }
export interface SourceRef { contentId: string; title: string; kind: 'OfficialSyllabus' | 'ApprovedMaterial' | 'AiGenerated' }
export interface AskTeacherResponse {
  reply: string; sources: SourceRef[]; groundedInApprovedContent: boolean; remainingToday: number
  conversationId?: string | null; safetyNotice?: string | null; messageId?: string | null
}
export interface ConversationSummary { id: string; title: string; subjectId: string; chapterId: string | null; language: string; lastMessageAtUtc: string; messageCount: number }
export interface Conversation { id: string; title: string; subjectId: string; chapterId: string | null; language: string
  messages: { id: string; role: 'user' | 'assistant'; text: string; sources: SourceRef[]; grounded: boolean; createdAtUtc: string; hasImage: boolean; helpful: boolean | null }[] }

// ---- AI quality (admin)
export interface AiUsage {
  days: number; usdToInr: number
  totals: { calls: number; inputTokens: number; outputTokens: number; speechCharacters: number; costUsd: number; costInr: number }
  byFeature: { feature: string; calls: number; tokens: number; costUsd: number }[]
  daily: { day: string; calls: number; costUsd: number }[]
  helpful: number; notHelpful: number
  recentNotHelpful: { messageId: string; question: string; answer: string; comment: string | null; at: string; language: string }[]
}
export interface EvalCase {
  id: string; subjectId: string; subjectName: string; chapterId: string | null; chapterTitle: string | null; question: string; language: Lang
  mode: TeacherMode; expectedKeywords: string | null; forbiddenPhrases: string | null; expectGrounded: boolean
}
export interface EvalCheck { name: string; passed: boolean; detail: string | null }
export interface EvalResult { caseId: string; question: string; language: string; passed: boolean; score: number; answer: string; grounded: boolean; checks: EvalCheck[]; error: string | null }
export interface EvalRunSummary { id: string; createdAtUtc: string; promptVersion: string; model: string | null; total: number; passed: number; averageScore: number }
export interface EvalRun { run: EvalRunSummary; results: EvalResult[] }

export type PubStatus = 'Draft' | 'InReview' | 'Approved' | 'Published' | 'Archived'
export interface Account { email: string; roles: string[] }
export interface AdminChapter { id: string; title: string; sequence: number; status: PubStatus }
export interface AdminSubject { id: string; name: string; code: string; mediumLanguage: string; sequence: number; chapters: AdminChapter[] }
export interface AdminClass { id: string; gradeNumber: number; gradeName: string; subjects: AdminSubject[] }
export interface AdminCurriculum { id: string; name: string; academicYear: string; version: number; status: PubStatus; classes: AdminClass[] }
export interface AdminBoard { id: string; name: string; code: string; countryCode: string; isEntranceExam: boolean; curricula: AdminCurriculum[] }

// ---- Parents and consent (Phase 4)
export interface ParentCode { code: string; expiresAtUtc: string }
export interface StudentParent { linkId: string; relationship: string; parentEmail: string; status: string; consentGranted: boolean }
export interface Child {
  studentId: string; displayName: string; className: string | null; boardCode: string | null; academicYear: string | null
  level: number; totalXp: number; streakDays: number; lastActiveDate: string | null
  consentRequired: boolean; consentGranted: boolean; consentVersion: string | null; consentGrantedAtUtc: string | null
  recentXp: { points: number; reason: string; createdAtUtc: string }[]
}

// ---- Lessons and progress (Phases 5–6)
export interface ChapterProgress { id: string; title: string; sequence: number; totalLessons: number; completedLessons: number }
export interface LessonItem { id: string; title: string; sequence: number; estimatedMinutes: number; completed: boolean; hasAttachment: boolean }
export interface StudentChapter { id: string; title: string; sequence: number; subjectId: string; subjectName: string; lessons: LessonItem[] }
export interface StudentLesson {
  id: string; title: string; body: string; language: string; kind: string; estimatedMinutes: number
  chapterId: string; chapterTitle: string; subjectId: string; subjectName: string; completed: boolean
  attachmentFileName: string | null; attachmentBytes: number | null; previousLessonId: string | null; nextLessonId: string | null
}
export interface SubjectProgress { subjectId: string; name: string; code: string; totalLessons: number; completedLessons: number }
export interface LearningSummary { totalLessons: number; completedLessons: number; streakDays: number; subjects: SubjectProgress[] }
export interface CompleteLessonResult { xpAwarded: number; chapterCompleted: boolean; streakDays: number; totalXp: number; newBadges?: string[] }
export interface AdminLesson {
  id: string; title: string; sequence: number; language: string; kind: string; estimatedMinutes: number; status: PubStatus
  bodyLength: number; attachmentFileName: string | null; attachmentBytes: number | null; updatedAtUtc: string | null
}
export interface AdminLessonDetail {
  id: string; chapterId: string; title: string; sequence: number; body: string; language: string; kind: string
  estimatedMinutes: number; status: PubStatus; attachmentFileName: string | null; attachmentBytes: number | null
}

// ---- Quizzes (Phase 8)
export interface QuizQuestion { id: string; prompt: string; options: string[] }
export interface QuizStart { attemptId: string; chapterId: string; chapterTitle: string; questions: QuizQuestion[] }
export interface QuizReviewItem { questionId: string; prompt: string; options: string[]; chosen: number | null; correct: number; isCorrect: boolean; explanation: string | null }
export interface QuizResult { attemptId: string; score: number; total: number; percent: number; xpAwarded: number; streakDays: number; review: QuizReviewItem[]; newBadges?: string[] }
export interface QuizHistory { available: boolean; questionCount: number; attempts: { attemptId: string; submittedAtUtc: string; score: number; total: number; percent: number }[] }
export interface AdminQuestion { id: string; prompt: string; options: string[]; correctIndex: number; explanation: string | null; difficulty: number; language: string; source: string; status: PubStatus }

// ---- Badges and leaderboard (Phase 9)
export interface Badge { code: string; name: string; description: string; icon: string; earned: boolean; awardedAtUtc: string | null }
export interface LeaderboardRow { rank: number; name: string; xp: number; level: number; isMe: boolean }
export interface Leaderboard { period: 'week' | 'all'; className: string; rows: LeaderboardRow[]; me: LeaderboardRow | null; showingMe: boolean }

// ---- Teachers (Phase 10)
export interface Classroom { id: string; name: string; schoolName: string | null; classId: string; className: string; boardCode: string; academicYear: string; joinCode: string; joiningOpen: boolean; studentCount: number }
export interface ClassroomStudent { studentId: string; name: string; totalXp: number; level: number; streakDays: number; lastActiveDate: string | null; lessonsCompleted: number; quizzesTaken: number; quizAverage: number | null }
export interface ChapterInsight { chapterId: string; title: string; subject: string; sequence: number; lessonCount: number; completionRate: number; quizAttempts: number; quizAverage: number | null }
export interface ClassroomDetail { classroom: Classroom; totalLessons: number; students: ClassroomStudent[]; chapters: ChapterInsight[] }
export interface MyClassroom { id: string; name: string; schoolName: string | null; teacher: string }

// ---- Billing (Phase 11)
export interface PlanInfo { code: string; name: string; amountPaise: number; durationDays: number; aiQuestionsPerDay: number; isFree: boolean }
export interface SubscriptionInfo { planCode: string; planName: string; aiQuestionsPerDay: number; endsAtUtc: string | null; paymentsEnabled: boolean }
export interface CheckoutInfo { orderId: string; keyId: string; amountPaise: number; currency: string; planName: string; studentName: string; email: string | null }

// ---- Admin platform (Phase 12)
export interface DayPoint { day: string; value: number }
export interface Metrics {
  users: number; students: number; parents: number; teachers: number; activeStudents7d: number; publishedLessons: number; publishedQuestions: number
  quizAttempts7d: number; aiMessages7d: number; aiTokens7d: number; activeSubscriptions: number; revenuePaise30d: number
  aiMessagesDaily: DayPoint[]; lessonsCompletedDaily: DayPoint[]; newStudentsDaily: DayPoint[]
}
export interface AdminUser { id: string; email: string | null; createdAtUtc: string; roles: string[]; locked: boolean; hasStudentProfile: boolean }
export interface UsersPage { total: number; users: AdminUser[] }
export interface CsvImportResult { applied: boolean; rows: number; classesCreated: number; subjectsCreated: number; chaptersCreated: number; chaptersUpdated: number; errors: string[] }

// ---- Timed tests, written answers (Stage B)
export type ExamKind = 'Practice' | 'Mock' | 'ClassTest'
export interface ExamSummary {
  id: string; title: string; kind: ExamKind; subject: string | null; mcqCount: number; writtenCount: number; durationMinutes: number
  opensAtUtc: string | null; closesAtUtc: string | null; classroom: string | null; myAttemptId: string | null; submitted: boolean; percent: number | null; canStart: boolean
}
export interface ExamHistory { attemptId: string; examId: string; title: string; kind: ExamKind; startedAtUtc: string; submittedAtUtc: string | null; percent: number | null; provisional: boolean }
export interface WrittenPrompt { id: string; prompt: string; maxMarks: number }
export interface WrittenDraft { questionId: string; text: string }
export interface CriterionMark { criterion: string; awarded: number; max: number; comment: string | null }
export interface WrittenResult {
  answerId: string; questionId: string; prompt: string; answer: string; score: number | null; max: number; status: 'Provisional' | 'Confirmed' | 'NotMarked'
  criteria: CriterionMark[]; feedback: string | null; teacherComment: string | null; modelAnswer: string | null
}
export interface ExamResult {
  mcqScore: number; mcqTotal: number; writtenScore: number | null; writtenMax: number; percent: number | null; provisional: boolean; xpAwarded: number
  review: QuizReviewItem[]; written: WrittenResult[]; newBadges: string[]; late: boolean
}
export interface ExamAttempt {
  attemptId: string; examId: string; title: string; kind: ExamKind; startedAtUtc: string; deadlineUtc: string; serverNowUtc: string; submitted: boolean
  questions: QuizQuestion[]; written: WrittenPrompt[]; answers: (number | null)[]; writtenAnswers: WrittenDraft[]; result: ExamResult | null
}
export interface RubricCriterion { criterion: string; marks: number }
export interface AdminWrittenQuestion { id: string; prompt: string; modelAnswer: string | null; rubric: RubricCriterion[]; maxMarks: number; language: string; status: PubStatus }
export interface ExamAdmin {
  id: string; title: string; kind: ExamKind; classId: string; subjectId: string | null; chapterIds: string[]; mcqCount: number; writtenCount: number
  durationMinutes: number; opensAtUtc: string | null; closesAtUtc: string | null; status: PubStatus; attempts: number; submitted: number; average: number | null
}
export interface MarkingItem {
  answerId: string; attemptId: string; student: string; examTitle: string; submittedAtUtc: string; question: string; modelAnswer: string | null
  rubric: RubricCriterion[]; answer: string; maxMarks: number; aiScore: number | null; aiCriteria: CriterionMark[]; aiFeedback: string | null
  teacherScore: number | null; teacherComment: string | null; status: 'Provisional' | 'Confirmed' | 'NotMarked'
}

// ---- Mastery, plan, missions, bookmarks (Stage B)
export interface ChapterMastery { chapterId: string; title: string; lessonsTotal: number; lessonsDone: number; mastery: number | null; band: string; attempts: number; lastPracticedAtUtc: string | null; hasQuiz: boolean }
export interface SubjectMastery { subjectId: string; name: string; average: number | null; chapters: ChapterMastery[] }
export interface MasteryOverview { subjects: SubjectMastery[]; weakAreas: { chapterId: string; title: string; subject: string; mastery: number }[] }
export interface PlanTask { kind: 'lesson' | 'quiz' | 'ask'; title: string; reason: string; chapterId: string; lessonId: string | null; subject: string }
export interface PlanDay { date: string; tasks: PlanTask[] }
export interface Mission { code: string; title: string; progress: number; target: number; done: boolean }
export interface Challenge extends Mission { description: string; claimed: boolean }
export interface Missions { date: string; missions: Mission[]; allDone: boolean; claimed: boolean; bonusXp: number; weekStart: string; challenges: Challenge[]; challengeXp: number }
export interface Bookmark { id: string; kind: 'lesson' | 'answer'; refId: string; title: string; snippet: string | null; note: string | null; createdAtUtc: string; conversationId: string | null; subjectId: string | null }

// ---- Homework, heat map, parent reports, schools (Stage C)
export type AssignmentKind = 'Lesson' | 'Quiz' | 'Test' | 'Task'
export interface TeacherAssignment {
  id: string; title: string; instructions: string | null; kind: AssignmentKind; lessonId: string | null; chapterId: string | null; examId: string | null
  target: string | null; dueAtUtc: string | null; createdAtUtc: string; done: number; total: number
  students: { studentId: string; name: string; done: boolean; doneAtUtc: string | null; text: string | null }[]
}
export interface Homework {
  id: string; title: string; instructions: string | null; kind: AssignmentKind; classroom: string; lessonId: string | null; chapterId: string | null; examId: string | null
  target: string | null; dueAtUtc: string | null; done: boolean; doneAtUtc: string | null; overdue: boolean; myText: string | null
}
export interface Heatmap { chapters: { chapterId: string; title: string; subject: string; average: number | null }[]; students: { studentId: string; name: string; cells: (number | null)[] }[] }
export interface WeeklyReport {
  studentId: string; name: string; weekStart: string; weekEnd: string; lessonsCompleted: number; lessons: string[]; quizzes: number; tests: number
  averageScore: number | null; bestScore: number | null; xpGained: number; activeDays: number; streakDays: number; aiQuestions: number
  practised: { title: string; subject: string; mastery: number }[]; weakAreas: { title: string; subject: string; mastery: number }[]
  homeworkSet: number; homeworkDone: number; homeworkOverdue: number; lessonsLastWeek: number; xpLastWeek: number; emailEnabled: boolean; emailConfigured: boolean
}
export interface Person { userId: string; email: string; name: string | null; role: 'SchoolAdmin' | 'Teacher' }
export interface AdminSchool { id: string; name: string; code: string; city: string | null; divisions: number; students: number; admins: Person[] }
export interface SchoolOverview { id: string; name: string; code: string; city: string | null; divisions: number; students: number; teachers: number; classes: { id: string; label: string }[] }
export interface Division { id: string; name: string; classId: string; classLabel: string; joinCode: string; joiningOpen: boolean; students: number; allocations: number }
export interface DivisionDetail {
  division: Division
  students: { studentId: string; name: string; totalXp: number; level: number; streakDays: number; lastActiveDate: string | null; lessonsCompleted: number }[]
  allocations: { id: string; subjectId: string; subject: string; teacherUserId: string; teacher: string; classroomId: string }[]
  subjects: { id: string; name: string }[]
  teachers: Person[]
}
export interface Slot { dayOfWeek: number; period: number; subjectId: string | null; teacherUserId: string | null; startTime: string | null; endTime: string | null; label: string | null; subject?: string | null; teacher?: string | null; division?: string | null }
export interface MyTimetable { divisionId: string; division: string; school: string; slots: Slot[] }
