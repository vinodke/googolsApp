import type { ReactNode } from 'react'

/**
 * Renders lesson text: blank-line paragraphs, "## " headings, "- " bullet lists and **bold**.
 * Built from React elements only (no innerHTML), so lesson text can never inject markup.
 */
export function LessonBody({ text }: { text: string }) {
  const blocks = text.replace(/\r\n/g, '\n').split(/\n{2,}/).map(b => b.trim()).filter(Boolean)
  return (
    <div className="lesson-body">
      {blocks.map((b, i) => {
        if (b.startsWith('### ')) return <h4 key={i}>{inline(b.slice(4))}</h4>
        if (b.startsWith('## ') || b.startsWith('# ')) return <h3 key={i}>{inline(b.replace(/^#+\s*/, ''))}</h3>
        const lines = b.split('\n')
        if (lines.every(l => /^\s*[-*•]\s+/.test(l))) {
          return <ul key={i}>{lines.map((l, j) => <li key={j}>{inline(l.replace(/^\s*[-*•]\s+/, ''))}</li>)}</ul>
        }
        if (lines.every(l => /^\s*\d+[.)]\s+/.test(l))) {
          return <ol key={i}>{lines.map((l, j) => <li key={j}>{inline(l.replace(/^\s*\d+[.)]\s+/, ''))}</li>)}</ol>
        }
        return <p key={i}>{lines.map((l, j) => <span key={j}>{j > 0 && <br />}{inline(l)}</span>)}</p>
      })}
    </div>
  )
}

function inline(s: string): ReactNode[] {
  return s.split(/(\*\*[^*]+\*\*)/g).map((part, i) =>
    part.startsWith('**') && part.endsWith('**') && part.length > 4 ? <strong key={i}>{part.slice(2, -2)}</strong> : part)
}
