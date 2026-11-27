import { useEffect, useState } from 'react'

export type Theme = 'light' | 'dark'
const KEY = 'googols-theme'

function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem(KEY)
    if (saved === 'light' || saved === 'dark') return saved
  } catch { /* storage unavailable */ }
  return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

/** Light/dark theme stored per browser; applied as data-theme on <html>. */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(initialTheme)
  useEffect(() => {
    document.documentElement.dataset.theme = theme
    try { localStorage.setItem(KEY, theme) } catch { /* ignore */ }
  }, [theme])
  return { theme, toggle: () => setTheme(t => (t === 'dark' ? 'light' : 'dark')) }
}

/** XP needed to reach a level (mirrors XpAwardPolicy.LevelFor on the server). */
export const xpForLevel = (level: number) => (100 * (level - 1) * level) / 2
