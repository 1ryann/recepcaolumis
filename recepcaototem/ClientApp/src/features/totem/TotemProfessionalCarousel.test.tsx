import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { TotemProfessionalCarousel } from './TotemProfessionalCarousel'
import type { TotemProfessionalCardDto } from '../../api/modules'

vi.stubGlobal('matchMedia', (q: string) => ({
  matches: false, media: q, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  addListener: vi.fn(), removeListener: vi.fn(), onchange: null, dispatchEvent: vi.fn(),
}))

const people: TotemProfessionalCardDto[] = [
  { id: 'a', name: 'Ana Souza', profession: 'Fisioterapeuta', photoUrl: null, status: 'AVAILABLE' },
  { id: 'b', name: 'Bruno Lima', profession: 'Psicólogo', photoUrl: '/api/totem/professionals/b/photo', status: 'IN_SERVICE' },
  { id: 'c', name: 'Carla Reis', profession: 'Nutricionista', photoUrl: null, status: 'UNAVAILABLE' },
]

beforeEach(() => {
  // jsdom has no layout; stub the scrolling API the carousel uses.
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', { value: vi.fn(), writable: true })
})

test('renders one option per professional with status text + colour class (not colour alone)', () => {
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} />)
  const options = screen.getAllByRole('option')
  expect(options).toHaveLength(3)
  expect(within(options[0]).getByText('Disponível')).toBeInTheDocument()
  expect(within(options[1]).getByText('Em atendimento')).toBeInTheDocument()
  expect(within(options[2]).getByText('Indisponível')).toBeInTheDocument()
  expect(options[0].querySelector('.totem-status-ok')).not.toBeNull()
  expect(options[1].querySelector('.totem-status-busy')).not.toBeNull()
})

test('first professional is active initially and emitted', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  expect(screen.getAllByRole('option')[0]).toHaveAttribute('aria-selected', 'true')
  expect(onActiveChange).toHaveBeenCalledWith(people[0])
})

test('Próximo arrow and ArrowRight move the active card', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  fireEvent.click(screen.getByRole('button', { name: /próximo/i }))
  expect(onActiveChange).toHaveBeenLastCalledWith(people[1])
  fireEvent.keyDown(screen.getByRole('listbox'), { key: 'ArrowRight' })
  expect(onActiveChange).toHaveBeenLastCalledWith(people[2])
})

test('dots reflect and control the active card', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  fireEvent.click(screen.getByRole('button', { name: /ir para carla reis/i }))
  expect(onActiveChange).toHaveBeenLastCalledWith(people[2])
})

test('missing photo shows initials; broken photo falls back to initials', () => {
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} />)
  expect(screen.getByText('AS')).toBeInTheDocument()   // Ana Souza, no photo
  const img = screen.getByRole('img', { name: '' }) as HTMLImageElement // Bruno has a photo
  fireEvent.error(img)
  expect(screen.getByText('BL')).toBeInTheDocument()
})

test('viewport and cards permit native horizontal and vertical gestures', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  for (const selector of ['viewport', 'card']) {
    const rule = css.match(new RegExp('\\.totem-carousel-' + selector + '\\s*\\{([^}]*)\\}'))
    expect(rule).not.toBeNull()
    expect(rule![1]).toMatch(/touch-action:\s*pan-x pan-y\s*;/)
  }
  const source = readFileSync(resolve(process.cwd(),
    'src/features/totem/TotemProfessionalCarousel.tsx'), 'utf8')
  expect(source).not.toMatch(/onPointerDown|onPointerMove|onPointerEnd|setPointerCapture|releasePointerCapture|didDragRef/)
})

test('cards and edge padding share a responsive width', () => {
  const css = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')
  expect(css).toContain('--totem-card-width:')
  expect(css).toMatch(/flex:\s*0 0 var\(--totem-card-width\)/)
  expect(css).toContain('calc(50% - (var(--totem-card-width) / 2))')
  expect(css).toMatch(/min-height:\s*var\(--totem-stage-height\)/)
})

test('onScroll finds the nearest centre without scrolling back', () => {
  let frame: FrameRequestCallback = () => {}
  const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation(cb => { frame = cb; return 1 })
  const emit = vi.fn()
  const view = render(<TotemProfessionalCarousel professionals={people} onActiveChange={emit} />)
  const viewport = screen.getByRole('listbox')
  Object.defineProperty(viewport, 'clientWidth', { value: 400, configurable: true })
  screen.getAllByRole('option').forEach((card, index) => {
    Object.defineProperty(card, 'offsetLeft', { value: 100 + index * 220, configurable: true })
    Object.defineProperty(card, 'offsetWidth', { value: 200, configurable: true })
  })
  viewport.scrollLeft = 220
  emit.mockClear()
  vi.mocked(viewport.scrollTo).mockClear()
  fireEvent.scroll(viewport)
  act(() => frame(0))
  expect(emit).toHaveBeenLastCalledWith(people[1])
  expect(screen.getAllByRole('option')[1]).toHaveAttribute('aria-selected', 'true')
  expect(viewport.scrollTo).not.toHaveBeenCalled()
  view.unmount()
  raf.mockRestore()
})

test('onScroll ignores zero geometry', () => {
  let frame: FrameRequestCallback = () => {}
  const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation(cb => { frame = cb; return 1 })
  const emit = vi.fn()
  const view = render(<TotemProfessionalCarousel professionals={people} onActiveChange={emit} />)
  const viewport = screen.getByRole('listbox')
  emit.mockClear()
  fireEvent.scroll(viewport)
  act(() => frame(0))
  expect(emit).not.toHaveBeenCalled()
  view.unmount()
  raf.mockRestore()
})

test('onScroll coalesces scroll events before its animation frame', () => {
  const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation(() => 1)
  const view = render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} />)
  const viewport = screen.getByRole('listbox')
  fireEvent.scroll(viewport)
  fireEvent.scroll(viewport)
  expect(raf).toHaveBeenCalledTimes(1)
  view.unmount()
  raf.mockRestore()
})

test('unmount cancels a pending scroll animation frame', () => {
  const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation(() => 41)
  const cancel = vi.spyOn(window, 'cancelAnimationFrame')
  const view = render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} />)
  fireEvent.scroll(screen.getByRole('listbox'))
  view.unmount()
  expect(cancel).toHaveBeenCalledWith(41)
  raf.mockRestore()
  cancel.mockRestore()
})

test('Home, End, and ArrowLeft respect carousel boundaries', () => {
  const emit = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={emit} />)
  const viewport = screen.getByRole('listbox')
  fireEvent.keyDown(viewport, { key: 'ArrowLeft' })
  expect(emit).toHaveBeenCalledTimes(1)
  fireEvent.keyDown(viewport, { key: 'End' })
  expect(emit).toHaveBeenLastCalledWith(people[2])
  fireEvent.keyDown(viewport, { key: 'ArrowRight' })
  expect(emit).toHaveBeenCalledTimes(2)
  fireEvent.keyDown(viewport, { key: 'Home' })
  expect(emit).toHaveBeenLastCalledWith(people[0])
})

test('side click centres and active click continues', () => {
  const emit = vi.fn(), next = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={emit} onContinue={next} />)
  fireEvent.click(screen.getAllByRole('option')[1])
  expect(emit).toHaveBeenLastCalledWith(people[1])
  expect(next).not.toHaveBeenCalled()
  fireEvent.click(screen.getAllByRole('option')[1])
  expect(next).toHaveBeenCalledTimes(1)
})

test('the centred card offers an explicit Selecionar affordance and the side cards do not', () => {
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} onContinue={vi.fn()} />)
  const options = screen.getAllByRole('option')
  expect(within(options[0]).getByText('Selecionar')).toBeInTheDocument()
  expect(within(options[1]).queryByText('Selecionar')).toBeNull()
  expect(within(options[2]).queryByText('Selecionar')).toBeNull()
})

test('the Selecionar affordance is decoration, never a button nested inside the card button', () => {
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} onContinue={vi.fn()} />)
  const active = screen.getAllByRole('option')[0]
  // The card itself is the <button>; a nested <button> would be invalid HTML and would
  // swallow the card's own click. The affordance only looks like one.
  expect(active.querySelector('button')).toBeNull()
  expect(active.querySelector('.totem-carousel-select')).not.toBeNull()
})

test('a card with no onContinue shows no Selecionar affordance', () => {
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} />)
  expect(screen.queryByText('Selecionar')).toBeNull()
})

test('the status pill keeps its word on the redesigned card, not colour alone', () => {
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={vi.fn()} onContinue={vi.fn()} />)
  const active = screen.getAllByRole('option')[0]
  const pill = active.querySelector('.totem-carousel-status')
  expect(pill).not.toBeNull()
  expect(pill!.textContent).toContain('Disponível')
})

test('the redesigned sizes live in the page stylesheet so styles.css stays untouched', () => {
  const sheet = readFileSync(resolve(process.cwd(), 'src/styles.totem-professionals.css'), 'utf8')
  // The card is deliberately bigger than the clamp(280px, 28vw, 320px) of styles.css.
  const width = sheet.match(/--totem-card-width:\s*clamp\((\d+)px, [\d.]+vw, (\d+)px\)/)
  expect(width).not.toBeNull()
  expect(Number(width![1])).toBeGreaterThan(280)
  expect(Number(width![2])).toBeGreaterThan(320)
  expect(sheet).toMatch(/@media \(max-width: 900px\)/)
  expect(sheet).toMatch(/@media \(max-width: 560px\)/)
  expect(sheet).toMatch(/@media \(prefers-reduced-motion: reduce\)/)
})
