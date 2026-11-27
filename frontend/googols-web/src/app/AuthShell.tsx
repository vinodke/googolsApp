import type { ReactNode } from 'react'
import { Icon } from './Icons'
import { useTheme } from './theme'
import './layout.css'

/** Split screen for sign-in, sign-up and onboarding: brand panel on the left, form on the right. */
export function AuthShell({ children }: { children: ReactNode }) {
  useTheme()
  return (
    <div className="auth-page">
      <aside className="auth-art" aria-hidden="true">
        <div className="brand"><span className="brand-mark">G</span><span className="brand-name">Googols</span></div>
        <div>
          <h2>Learn Smarter. Grow Further.</h2>
          <ul className="auth-points">
            <li><Icon name="sparkles" /> An AI teacher that explains in Malayalam or English</li>
            <li><Icon name="book" /> Your own syllabus, class and subjects</li>
            <li><Icon name="trophy" /> Earn XP, level up and keep your streak</li>
          </ul>
        </div>
        <span style={{ opacity: 0.75, fontSize: 'var(--text-sm)', position: 'relative', zIndex: 1 }}>googols.in</span>
      </aside>
      <div className="auth-form-wrap fade-in">{children}</div>
    </div>
  )
}
