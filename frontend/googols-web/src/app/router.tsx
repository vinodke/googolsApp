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
import { AdminDashboardPage, AdminUsersPage } from '../features/admin/AdminPlatformPages'
import { QuizPage } from '../features/quiz/QuizPage'
import { AchievementsPage } from '../features/achievements/AchievementsPage'
import { ClassroomPage, TeacherHomePage, TeacherLayout } from '../features/teacher/TeacherPages'
import { PlansPage } from '../features/billing/PlansPage'
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
          { path: 'dashboard', element: <AdminDashboardPage /> },
          { path: 'users', element: <AdminUsersPage /> },
        ],
      },
      {
        path: '/teacher',
        element: <TeacherLayout />,
        children: [
          { index: true, element: <TeacherHomePage /> },
          { path: 'classrooms/:id', element: <ClassroomPage /> },
        ],
      },
      {
        path: '/parent',
        element: <ParentLayout />,
        children: [
          { index: true, element: <ParentHomePage /> },
          { path: 'link', element: <LinkChildPage /> },
          { path: 'plans', element: <PlansPage /> },
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
          { path: 'achievements', element: <AchievementsPage /> },
          { path: 'plans', element: <PlansPage /> },
          { path: 'chapters/:chapterId', element: <ChapterPage /> },
          { path: 'chapters/:chapterId/quiz', element: <QuizPage /> },
          { path: 'lessons/:lessonId', element: <LessonPage /> },
        ],
      },
    ],
  },
  { path: '*', element: <Navigate to="/" replace /> },
])
