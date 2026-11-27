import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api/client'
import type { Bookmark } from './api/types'

export function useBookmarks(enabled = true) {
  return useQuery({ queryKey: ['learning', 'bookmarks'], queryFn: () => api.get<Bookmark[]>('/learning/bookmarks'), enabled })
}

/** Save / unsave a lesson or an AI answer. */
export function useBookmarkToggle(kind: Bookmark['kind'], refId: string | undefined) {
  const qc = useQueryClient()
  const list = useBookmarks(!!refId)
  const existing = list.data?.find(b => b.kind === kind && b.refId === refId)
  const toggle = useMutation({
    mutationFn: () => (existing ? api.del(`/learning/bookmarks/${existing.id}`) : api.post('/learning/bookmarks', { kind, refId })),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['learning', 'bookmarks'] }),
  })
  return { saved: !!existing, toggle: () => toggle.mutate(), pending: toggle.isPending }
}
