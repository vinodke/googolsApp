import { Navigate, Outlet, createBrowserRouter } from 'react-router'
import { useAuth } from '../core/auth/auth'
import { AiTeacherPage } from '../features/ai-teacher/AiTeacherPage'
import { LoginPage, RegisterPage } from '../features/auth/AuthPages'
import { OnboardingPage } from '../features/onboarding/OnboardingPage'
import { DashboardPage } from '../features/student/DashboardPage'
import { SubjectsPage } from '../features/subjects/SubjectsPages'
import { ChapterPage, LessonPage, SubjectChaptersPage } from '../features/lessons/LessonPages'
import { AdminCurriculumPage } from '../features/admin/AdminCurriculumPage'
import { AdminLessonsPage } from '../features/admin/AdminLessonsPage'
import { AdminQuestionsPage } from '../features/admin/AdminQuestionsPage'
import { QuizPage } from '../features/quiz/QuizPage'
import { FamilyPage } from '../features/family/FamilyPage'
import { LinkChildPage, ParentHomePage, ParentLayout } from '../features/parent/ParentPages'
import { AdminLayout } from './AdminLayout'
import { StudentLayout } from './StudentLayout'

function RequireAuth() {
  const { user, loading } = useAuth()
  if (loading) return <div className="page-loading" aria-busy="true" />
  return user ? <Outlet /> : <Navigate to="/login" replace />
}

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  {
    element: <RequireAuth />,
    children: [
      { path: '/onboarding', element: <OnboardingPage /> },
      {
        path: '/admin',
        element: <AdminLayout />,
        children: [
          { index: true, element: <AdminCurriculumPage /> },
          { path: 'chapters/:chapterId/lessons', element: <AdminLessonsPage /> },
          { path: 'chapters/:chapterId/questions', element: <AdminQuestionsPage /> },
        ],
      },
      {
        path: '/parent',
        element: <ParentLayout />,
        children: [
          { index: true, element: <ParentHomePage /> },
          { path: 'link', element: <LinkChildPage /> },
        ],
      },
      {
        element: <StudentLayout />,
        children: [
          { index: true, element: <DashboardPage /> },
          { path: 'subjects', element: <SubjectsPage /> },
          { path: 'subjects/:subjectId', element: <SubjectChaptersPage /> },
          { path: 'ai-teacher', element: <AiTeacherPage /> },
          { path: 'family', element: <FamilyPage /> },
          { path: 'chapters/:chapterId', element: <ChapterPage /> },
          { path: 'chapters/:chapterId/quiz', element: <QuizPage /> },
          { path: 'lessons/:lessonId', element: <LessonPage /> },
        ],
      },
    ],
  },
  { path: '*', element: <Navigate to="/" replace /> },
])
