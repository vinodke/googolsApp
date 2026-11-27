import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Icon, type IconName } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { Badge, Leaderboard } from '../../core/api/types'
import './achievements.css'

const ICONS: Record<string, IconName> = { book: 'book', grad: 'grad', trophy: 'trophy', bolt: 'bolt', star: 'star', flame: 'flame' }

export function AchievementsPage() {
  const qc = useQueryClient()
  const [period, setPeriod] = useState<'week' | 'all'>('week')
  const badges = useQuery({ queryKey: ['badges'], queryFn: () => api.get<Badge[]>('/gamification/badges') })
  const board = useQuery({ queryKey: ['leaderboard', period], queryFn: () => api.get<Leaderboard>(`/gamification/leaderboard?period=${period}`) })
  const visibility = useMutation({
    mutationFn: (visible: boolean) => api.put('/gamification/leaderboard/visibility', { visible }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['leaderboard'] }),
  })
  const earned = badges.data?.filter(b => b.earned).length ?? 0

  return (
    <div className="stack-lg">
      <header className="page-head">
        <div>
          <h1>Rewards</h1>
          <p className="muted">Badges you've earned and how your class is doing.</p>
        </div>
      </header>

      <div className="grid-2">
        <section className="card">
          <div className="card-title"><h2>Badges</h2><span className="badge">{earned} / {badges.data?.length ?? 0}</span></div>
          {badges.isLoading && <div className="skeleton" style={{ height: 160 }} />}
          <div className="badge-grid">
            {badges.data?.map(b => (
              <div key={b.code} className={`badge-card${b.earned ? ' earned' : ''}`} title={b.description}>
                <span className="badge-medal" aria-hidden><Icon name={ICONS[b.icon] ?? 'trophy'} size={24} /></span>
                <strong>{b.name}</strong>
                <span className="small muted">{b.earned && b.awardedAtUtc ? new Date(b.awardedAtUtc).toLocaleDateString() : b.description}</span>
                {!b.earned && <span className="sr-only">Locked</span>}
              </div>
            ))}
          </div>
        </section>

        <section className="card">
          <div className="card-title">
            <h2>{board.data?.className ?? 'Class'} leaderboard</h2>
            <div className="segmented" role="group" aria-label="Period">
              <button type="button" aria-pressed={period === 'week'} onClick={() => setPeriod('week')}>This week</button>
              <button type="button" aria-pressed={period === 'all'} onClick={() => setPeriod('all')}>All time</button>
            </div>
          </div>
          {board.isError && <div className="alert" role="alert">{errorMessage(board.error)}</div>}
          <ol className="list leaderboard">
            {board.data?.rows.map(r => (
              <li key={`${r.rank}-${r.name}`} className={`list-item${r.isMe ? ' me' : ''}`}>
                <span className={`rank rank-${r.rank}`}>{r.rank}</span>
                <span className="grow"><strong>{r.name}{r.isMe ? ' (you)' : ''}</strong><span className="small muted" style={{ display: 'block' }}>Level {r.level}</span></span>
                <strong>{r.xp} XP</strong>
              </li>
            ))}
          </ol>
          {board.data && board.data.rows.length === 0 && <p className="muted small">No XP earned yet {period === 'week' ? 'this week' : ''}. Finish a lesson to get on the board!</p>}
          {board.data?.me && !board.data.rows.some(r => r.isMe) && (
            <p className="small muted">Your rank: {board.data.me.rank} ({board.data.me.xp} XP){board.data.showingMe ? '' : ' · hidden from classmates'}</p>
          )}
          {board.data && (
            <label className="check" style={{ marginTop: 'var(--space-3)' }}>
              <input type="checkbox" checked={board.data.showingMe} disabled={visibility.isPending} onChange={e => visibility.mutate(e.target.checked)} />
              Show me on my class leaderboard (classmates see your first name and initial only)
            </label>
          )}
        </section>
      </div>
    </div>
  )
}
