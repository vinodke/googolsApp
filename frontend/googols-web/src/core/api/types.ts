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
export interface AskTeacherResponse { reply: string; sources: SourceRef[]; groundedInApprovedContent: boolean; remainingToday: number }

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
export interface CompleteLessonResult { xpAwarded: number; chapterCompleted: boolean; streakDays: number; totalXp: number }
export interface AdminLesson {
  id: string; title: string; sequence: number; language: string; kind: string; estimatedMinutes: number; status: PubStatus
  bodyLength: number; attachmentFileName: string | null; attachmentBytes: number | null; updatedAtUtc: string | null
}
export interface AdminLessonDetail {
  id: string; chapterId: string; title: string; sequence: number; body: string; language: string; kind: string
  estimatedMinutes: number; status: PubStatus; attachmentFileName: string | null; attachmentBytes: number | null
}
