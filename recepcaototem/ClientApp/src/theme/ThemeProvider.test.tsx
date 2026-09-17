import { act, render, screen } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import { THEME_STORAGE_KEY, ThemeProvider, useTheme } from './ThemeProvider'

function Consumer() {
  const { theme, setTheme, toggleTheme } = useTheme()
  return (
    <div>
      <span>current:{theme}</span>
      <button onClick={() => setTheme('dark')}>set-dark</button>
      <button onClick={toggleTheme}>toggle</button>
    </div>
  )
}

// Stubs `window.matchMedia` with a fake MediaQueryList whose `matches` can be flipped and whose
// 'change' listeners can be fired from the test, mirroring the browser API ThemeProvider consumes.
function stubMatchMedia(initialMatches: boolean) {
  const listeners = new Set<(event: MediaQueryListEvent) => void>()
  const mediaQueryList = {
    matches: initialMatches,
    media: '(prefers-color-scheme: dark)',
    addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => { listeners.add(listener) },
    removeEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => { listeners.delete(listener) },
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
    onchange: null,
  }
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue(mediaQueryList))
  return {
    fireChange(matches: boolean) {
      mediaQueryList.matches = matches
      listeners.forEach((listener) => listener({ matches } as MediaQueryListEvent))
    },
  }
}

test('defaults to the stored theme preference when it is valid', () => {
  localStorage.setItem(THEME_STORAGE_KEY, 'dark')
  stubMatchMedia(false)
  render(<ThemeProvider><Consumer /></ThemeProvider>)
  expect(screen.getByText('current:dark')).toBeInTheDocument()
})

test('falls back to the system preference when nothing is stored', () => {
  stubMatchMedia(true)
  render(<ThemeProvider><Consumer /></ThemeProvider>)
  expect(screen.getByText('current:dark')).toBeInTheDocument()
})

test('an invalid stored value is treated as no manual preference', () => {
  localStorage.setItem(THEME_STORAGE_KEY, 'sepia')
  stubMatchMedia(true)
  render(<ThemeProvider><Consumer /></ThemeProvider>)
  expect(screen.getByText('current:dark')).toBeInTheDocument()
})

test('toggling the theme updates the document dataset and persists the choice to localStorage', () => {
  stubMatchMedia(false)
  render(<ThemeProvider><Consumer /></ThemeProvider>)
  expect(document.documentElement.dataset.theme).toBe('light')
  act(() => { screen.getByText('toggle').click() })
  expect(screen.getByText('current:dark')).toBeInTheDocument()
  expect(document.documentElement.dataset.theme).toBe('dark')
  expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
})

test('setTheme updates the document dataset and persists the choice to localStorage', () => {
  stubMatchMedia(false)
  render(<ThemeProvider><Consumer /></ThemeProvider>)
  act(() => { screen.getByText('set-dark').click() })
  expect(screen.getByText('current:dark')).toBeInTheDocument()
  expect(document.documentElement.dataset.theme).toBe('dark')
  expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
})

test('a system preference change updates the theme only when nothing is stored manually', () => {
  const media = stubMatchMedia(false)
  render(<ThemeProvider><Consumer /></ThemeProvider>)
  expect(screen.getByText('current:light')).toBeInTheDocument()
  act(() => { media.fireChange(true) })
  expect(screen.getByText('current:dark')).toBeInTheDocument()
  expect(localStorage.getItem(THEME_STORAGE_KEY)).toBeNull() // following the system is not "choosing"
})

test('a system preference change is ignored once the user has chosen a theme manually', () => {
  const media = stubMatchMedia(false)
  render(<ThemeProvider><Consumer /></ThemeProvider>)
  act(() => { screen.getByText('set-dark').click() })
  expect(screen.getByText('current:dark')).toBeInTheDocument()
  act(() => { media.fireChange(false) }) // system reverts to light; manual choice must win
  expect(screen.getByText('current:dark')).toBeInTheDocument()
})

test('never throws when localStorage access throws, falling back to the system preference gracefully', () => {
  stubMatchMedia(true)
  const getItemSpy = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
  const setItemSpy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked') })
  expect(() => render(<ThemeProvider><Consumer /></ThemeProvider>)).not.toThrow()
  expect(screen.getByText('current:dark')).toBeInTheDocument()
  expect(() => act(() => { screen.getByText('toggle').click() })).not.toThrow()
  getItemSpy.mockRestore()
  setItemSpy.mockRestore()
})

test('toggling the theme updates the theme-color meta tag to match --lumis-bg for the new theme', () => {
  const meta = document.createElement('meta')
  meta.setAttribute('name', 'theme-color')
  meta.setAttribute('content', '#f2f2f2')
  document.head.appendChild(meta)
  try {
    stubMatchMedia(false)
    render(<ThemeProvider><Consumer /></ThemeProvider>)
    expect(meta.getAttribute('content')).toBe('#f2f2f2')
    act(() => { screen.getByText('toggle').click() })
    expect(meta.getAttribute('content')).toBe('#181818')
  } finally {
    meta.remove()
  }
})

test('never throws when the theme-color meta tag is missing from the document', () => {
  stubMatchMedia(false)
  render(<ThemeProvider><Consumer /></ThemeProvider>)
  expect(() => act(() => { screen.getByText('toggle').click() })).not.toThrow()
})

test('useTheme throws a clear error when used outside a ThemeProvider', () => {
  const spy = vi.spyOn(console, 'error').mockImplementation(() => {})
  expect(() => render(<Consumer />)).toThrow('useTheme must be used inside ThemeProvider')
  spy.mockRestore()
})
