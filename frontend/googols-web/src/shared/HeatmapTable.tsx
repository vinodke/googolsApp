import type { Heatmap } from '../core/api/types'
import './heatmap.css'

function tone(v: number | null) {
  if (v === null) return 'hm-none'
  if (v < 40) return 'hm-low'
  if (v < 60) return 'hm-mid-low'
  if (v < 75) return 'hm-mid'
  return 'hm-high'
}

/** Students × chapters, coloured by mastery. Empty cells mean the student hasn't been tested on that chapter yet. */
export function HeatmapTable({ data }: { data: Heatmap }) {
  if (data.students.length === 0) return <p className="muted">No students yet.</p>
  if (data.chapters.length === 0) return <p className="muted">No published chapters yet.</p>
  return (
    <div className="hm-wrap">
      <table className="hm" aria-label="Mastery heat map">
        <thead>
          <tr>
            <th scope="col">Student</th>
            {data.chapters.map(c => <th key={c.chapterId} scope="col" title={`${c.subject}: ${c.title}`}><span>{c.title}</span></th>)}
          </tr>
        </thead>
        <tbody>
          {data.students.map(s => (
            <tr key={s.studentId}>
              <th scope="row">{s.name}</th>
              {s.cells.map((v, i) => (
                <td key={data.chapters[i].chapterId} className={tone(v)} title={`${s.name} · ${data.chapters[i].title}: ${v === null ? 'not tested' : `${v}%`}`}>
                  {v === null ? '' : Math.round(v)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row">Class average</th>
            {data.chapters.map(c => <td key={c.chapterId} className={tone(c.average)}>{c.average ?? '—'}</td>)}
          </tr>
        </tfoot>
      </table>
      <div className="hm-legend small muted">
        <span><i className="hm-low" /> below 40%</span><span><i className="hm-mid-low" /> 40–59%</span><span><i className="hm-mid" /> 60–74%</span>
        <span><i className="hm-high" /> 75%+</span><span><i className="hm-none" /> not tested</span>
      </div>
    </div>
  )
}
