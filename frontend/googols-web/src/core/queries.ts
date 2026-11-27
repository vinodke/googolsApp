import { useQuery } from '@tanstack/react-query'
import { ApiError, api } from './api/client'
import type { Account, Child, StudentMe, Subject, XpSummary } from './api/types'

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

export function isParent(account: Account | undefined) {
  return !!account?.roles.includes('Parent')
}

export function useChildren(enabled = true) {
  return useQuery({ queryKey: ['parent', 'children'], queryFn: () => api.get<Child[]>('/parents/children'), enabled })
}

/** Parental consent notice version; must match ParentalConsent.CurrentVersion on the server. */
export const CONSENT_VERSION = '2026-11'
