import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
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
  // jsdom has no layout; stub the geometry / pointer-capture the carousel touches.
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', { value: vi.fn(), writable: true })
  Object.defineProperty(HTMLElement.prototype, 'scrollBy', { value: vi.fn(), writable: true })
  Object.defineProperty(HTMLElement.prototype, 'setPointerCapture', { value: vi.fn(), writable: true })
  Object.defineProperty(HTMLElement.prototype, 'releasePointerCapture', { value: vi.fn(), writable: true })
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

test('mouse drag scrolls and suppresses the click-select', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  const viewport = screen.getByRole('listbox')
  fireEvent.pointerDown(viewport, { pointerType: 'mouse', pointerId: 1, clientX: 300 })
  fireEvent.pointerMove(viewport, { pointerType: 'mouse', pointerId: 1, clientX: 120 })
  fireEvent.pointerUp(viewport, { pointerType: 'mouse', pointerId: 1, clientX: 120 })
  onActiveChange.mockClear()
  fireEvent.click(screen.getAllByRole('option')[2])   // click right after a drag
  expect(onActiveChange).not.toHaveBeenCalled()
})

test('a touch swipe moves the active card left and right (no arrows needed)', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  const viewport = screen.getByRole('listbox')
  onActiveChange.mockClear() // ignore the initial emit

  // finger drags left across the strip -> next professional
  fireEvent.pointerDown(viewport, { pointerType: 'touch', pointerId: 1, clientX: 300 })
  fireEvent.pointerMove(viewport, { pointerType: 'touch', pointerId: 1, clientX: 230 })
  fireEvent.pointerMove(viewport, { pointerType: 'touch', pointerId: 1, clientX: 180 })
  fireEvent.pointerUp(viewport, { pointerType: 'touch', pointerId: 1, clientX: 180 })
  expect(onActiveChange).toHaveBeenLastCalledWith(people[1])
  expect(screen.getAllByRole('option')[1]).toHaveAttribute('aria-selected', 'true')

  // finger drags right -> previous professional
  fireEvent.pointerDown(viewport, { pointerType: 'touch', pointerId: 2, clientX: 180 })
  fireEvent.pointerMove(viewport, { pointerType: 'touch', pointerId: 2, clientX: 300 })
  fireEvent.pointerUp(viewport, { pointerType: 'touch', pointerId: 2, clientX: 300 })
  expect(onActiveChange).toHaveBeenLastCalledWith(people[0])
})

test('a long touch fling can jump more than one card', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  const viewport = screen.getByRole('listbox')
  onActiveChange.mockClear()
  fireEvent.pointerDown(viewport, { pointerType: 'touch', pointerId: 1, clientX: 500 })
  fireEvent.pointerMove(viewport, { pointerType: 'touch', pointerId: 1, clientX: 60 })
  fireEvent.pointerUp(viewport, { pointerType: 'touch', pointerId: 1, clientX: 60 })
  expect(onActiveChange).toHaveBeenLastCalledWith(people[2]) // 440px net ≈ 2 cards
})

test('a tiny touch drag stays a tap and does not change the card', () => {
  const onActiveChange = vi.fn()
  render(<TotemProfessionalCarousel professionals={people} onActiveChange={onActiveChange} />)
  const viewport = screen.getByRole('listbox')
  onActiveChange.mockClear()
  fireEvent.pointerDown(viewport, { pointerType: 'touch', pointerId: 1, clientX: 300 })
  fireEvent.pointerMove(viewport, { pointerType: 'touch', pointerId: 1, clientX: 297 })
  fireEvent.pointerUp(viewport, { pointerType: 'touch', pointerId: 1, clientX: 297 })
  expect(onActiveChange).not.toHaveBeenCalled()
})
