import { useQuery } from '@tanstack/react-query'
import { ApiError, api } from './api/client'
import type { Account, StudentMe, Subject, XpSummary } from './api/types'

export function useStudentMe() {
  return useQuery({
    queryKey: ['student', 'me'],
    queryFn: async () => {
      try {
        return await api.get<StudentMe>('/students/me')
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) return null // signed in, onboarding not done
        throw e
      }
    },
  })
}

export function useSubjects(classId: string | undefined) {
  return useQuery({
    queryKey: ['subjects', classId],
    queryFn: () => api.get<Subject[]>(`/curriculum/classes/${classId}/subjects`),
    enabled: !!classId,
  })
}

export function useXp() {
  return useQuery({ queryKey: ['xp'], queryFn: () => api.get<XpSummary>('/gamification/xp') })
}

export const ADMIN_ROLES = ['SuperAdmin', 'Admin', 'ContentManager']

export function useAccount() {
  return useQuery({ queryKey: ['account'], queryFn: () => api.get<Account>('/account/me') })
}

export function isAdmin(account: Account | undefined) {
  return !!account?.roles.some(r => ADMIN_ROLES.includes(r))
}
