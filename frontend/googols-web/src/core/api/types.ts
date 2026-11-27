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
  conversationId?: string | null; safetyNotice?: string | null
}
export interface ConversationSummary { id: string; title: string; subjectId: string; chapterId: string | null; language: string; lastMessageAtUtc: string; messageCount: number }
export interface Conversation { id: string; title: string; subjectId: string; chapterId: string | null; language: string
  messages: { role: 'user' | 'assistant'; text: string; sources: SourceRef[]; grounded: boolean; createdAtUtc: string }[] }

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
