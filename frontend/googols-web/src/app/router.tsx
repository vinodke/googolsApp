import { Navigate, Outlet, createBrowserRouter } from 'react-router'
import { useAuth } from '../core/auth/auth'
import { AiTeacherPage } from '../features/ai-teacher/AiTeacherPage'
import { LoginPage, RegisterPage } from '../features/auth/AuthPages'
import { OnboardingPage } from '../features/onboarding/OnboardingPage'
import { DashboardPage } from '../features/student/DashboardPage'
import { SubjectChaptersPage, SubjectsPage } from '../features/subjects/SubjectsPages'
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
        element: <StudentLayout />,
        children: [
          { index: true, element: <DashboardPage /> },
          { path: 'subjects', element: <SubjectsPage /> },
          { path: 'subjects/:subjectId', element: <SubjectChaptersPage /> },
          { path: 'ai-teacher', element: <AiTeacherPage /> },
        ],
      },
    ],
  },
  { path: '*', element: <Navigate to="/" replace /> },
])
