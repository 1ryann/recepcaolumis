import { createContext, type ReactNode, useCallback, useContext, useEffect, useState } from 'react'

export type Theme = 'light' | 'dark'

export const THEME_STORAGE_KEY = 'lumis-theme'

type ThemeContextValue = {
  theme: Theme
  setTheme(theme: Theme): void
  toggleTheme(): void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

// Anything other than exactly 'light'/'dark' (missing key, corrupted value, storage blocked)
// means "no manual preference" — fall through to the system setting instead.
function readStoredTheme(): Theme | null {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY)
    return stored === 'light' || stored === 'dark' ? stored : null
  } catch {
    return null
  }
}

function writeStoredTheme(theme: Theme): void {
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme)
  } catch {
    // Storage full/blocked (private browsing, locked-down context) — the app still works,
    // it just won't remember the choice across reloads.
  }
}

export function getSystemTheme(): Theme {
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
    }
  } catch {
    // matchMedia can throw in some locked-down/embedded contexts — fall through to dark.
  }
  // No usable matchMedia — mirror index.html's FOUC-script catch block, which falls back to
  // 'dark' (preserve dark identity as the safe default) rather than assuming 'light'.
  return 'dark'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(() => readStoredTheme() ?? getSystemTheme())

  // Keep the DOM in sync for the lifetime of the SPA. The FOUC script in index.html already set
  // this correctly before first paint; this effect just guarantees it stays correct afterward.
  useEffect(() => {
    document.documentElement.dataset.theme = theme
  }, [theme])

  // Mirrors index.html's inline FOUC script, but for LIVE theme changes (a manual toggle, or the
  // OS preference flipping) instead of first paint — that script only runs once, before React
  // ever mounts. Browser chrome (the mobile status bar / task-switcher color) has no paint of its
  // own to flash, so there is no FOUC risk here; this just keeps it from going stale after the
  // initial load. Guarded defensively since the meta tag may be absent in tests/jsdom.
  useEffect(() => {
    try {
      const meta = document.querySelector('meta[name="theme-color"]')
      if (meta) meta.setAttribute('content', theme === 'dark' ? '#181818' : '#f2f2f2')
    } catch {
      // Defensive only — matches the rest of this module's tolerance for locked-down DOM access.
    }
  }, [theme])

  const setTheme = useCallback((next: Theme) => {
    setThemeState(next)
    writeStoredTheme(next)
  }, [])

  const toggleTheme = useCallback(() => {
    setThemeState((current) => {
      const next = current === 'dark' ? 'light' : 'dark'
      writeStoredTheme(next)
      return next
    })
  }, [])

  // System-preference live-follow: only relevant while the user hasn't made a manual choice.
  // Re-checks localStorage inside the handler itself (not just at mount) so a manual toggle
  // made after this listener was attached is still respected without re-subscribing.
  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return
    let mediaQuery: MediaQueryList
    try {
      mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    } catch {
      return
    }
    const handleChange = (event: MediaQueryListEvent) => {
      if (readStoredTheme() !== null) return // a manual preference exists — never override it
      setThemeState(event.matches ? 'dark' : 'light') // never persisted: this is not a "choice"
    }
    mediaQuery.addEventListener?.('change', handleChange)
    return () => mediaQuery.removeEventListener?.('change', handleChange)
  }, [])

  return <ThemeContext.Provider value={{ theme, setTheme, toggleTheme }}>{children}</ThemeContext.Provider>
}

export function useTheme() {
  const value = useContext(ThemeContext)
  if (!value) throw new Error('useTheme must be used inside ThemeProvider')
  return value
}
