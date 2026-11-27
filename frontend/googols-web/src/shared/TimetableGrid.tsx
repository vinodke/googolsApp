import type { Slot } from '../core/api/types'

export const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

/** Read-only week grid (days × periods). */
export function TimetableGrid({ slots, show = 'subject' }: { slots: Slot[]; show?: 'subject' | 'division' }) {
  if (slots.length === 0) return <p className="muted">No timetable yet.</p>
  const periods = Math.max(...slots.map(s => s.period))
  const days = slots.some(s => s.dayOfWeek === 6) ? 6 : 5
  const at = (d: number, p: number) => slots.find(s => s.dayOfWeek === d && s.period === p)
  const today = new Date().getDay() // 0 = Sunday
  return (
    <div className="hm-wrap">
      <table className="table tt" aria-label="Timetable">
        <thead><tr><th>Period</th>{DAYS.slice(0, days).map((d, i) => <th key={d} className={today === i + 1 ? 'tt-today' : ''}>{d}</th>)}</tr></thead>
        <tbody>
          {Array.from({ length: periods }, (_, i) => i + 1).map(p => (
            <tr key={p}>
              <th>{p}</th>
              {Array.from({ length: days }, (_, i) => i + 1).map(d => {
                const s = at(d, p)
                return (
                  <td key={d} className={today === d ? 'tt-today' : ''}>
                    {s && <><strong>{show === 'division' ? `${s.division ?? ''} ${s.subject ?? ''}` : (s.subject ?? s.label)}</strong>
                      <div className="small muted">{show === 'subject' && s.teacher ? s.teacher : ''}{s.startTime ? ` ${s.startTime}–${s.endTime ?? ''}` : ''}</div></>}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
