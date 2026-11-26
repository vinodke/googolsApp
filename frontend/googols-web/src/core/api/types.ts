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
