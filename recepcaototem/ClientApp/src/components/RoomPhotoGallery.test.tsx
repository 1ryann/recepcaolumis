import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { RoomPhotoGallery } from './RoomPhotoGallery'

// Autoplay (5 s) is faked throughout — `advance` drives it deterministically instead of
// waiting on real time. Interactions use `fireEvent` (the convention already used across
// this codebase — see TotemHandoff.test.tsx/TotemProfessionalCarousel.test.tsx — the
// project has no @testing-library/user-event dependency).
beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  vi.runOnlyPendingTimers()
  vi.useRealTimers()
})

const advance = (ms: number) => act(() => { vi.advanceTimersByTime(ms) })

const photos = [
  'https://cdn.example/sala-1.jpg',
  'https://cdn.example/sala-2.jpg',
  'https://cdn.example/sala-3.jpg',
]

test('with zero photos shows only the door fallback icon and nothing clickable', () => {
  render(<RoomPhotoGallery photoUrls={[]} roomName="Sala Alfa" />)
  expect(screen.getByTestId('totem-room-detail-fallback')).toBeInTheDocument()
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
})

test('with exactly one photo shows the main photo without thumbnails or dots', () => {
  render(<RoomPhotoGallery photoUrls={[photos[0]]} roomName="Sala Alfa" />)
  const main = screen.getByAltText('Foto da sala Sala Alfa')
  expect(main).toHaveAttribute('src', photos[0])
  expect(screen.queryByRole('button', { name: /ver foto/i })).not.toBeInTheDocument()
  expect(screen.queryByRole('group', { name: /selecionar foto/i })).not.toBeInTheDocument()
})

test('with N photos renders the first photo plus a thumbnail per photo', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  const main = screen.getByAltText('Foto da sala Sala Alfa')
  expect(main).toHaveAttribute('src', photos[0])
  expect(screen.getAllByRole('button', { name: /ver foto/i })).toHaveLength(3)
})

test('clicking a thumbnail swaps the main photo to match', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  const thumbs = screen.getAllByRole('button', { name: /ver foto/i })
  fireEvent.click(thumbs[1])
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toHaveAttribute('src', photos[1])
})

test('clicking a thumbnail also opens the lightbox on that photo', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  const thumbs = screen.getAllByRole('button', { name: /ver foto/i })
  fireEvent.click(thumbs[2])
  const dialog = screen.getByRole('dialog')
  expect(dialog).toBeInTheDocument()
  expect(screen.getByAltText('Foto ampliada da sala Sala Alfa')).toHaveAttribute('src', photos[2])
})

test('clicking the main photo opens the lightbox on the currently selected photo', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  fireEvent.click(screen.getByRole('button', { name: /ampliar foto 1/i }))
  expect(screen.getByRole('dialog')).toBeInTheDocument()
  expect(screen.getByAltText('Foto ampliada da sala Sala Alfa')).toHaveAttribute('src', photos[0])
})

test('the lightbox closes via the close button', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  fireEvent.click(screen.getByRole('button', { name: /ampliar foto 1/i }))
  fireEvent.click(screen.getByRole('button', { name: 'Fechar' }))
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})

test('the lightbox closes via the Escape key', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  fireEvent.click(screen.getByRole('button', { name: /ampliar foto 1/i }))
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})

test('the lightbox next/prev buttons navigate and wrap around', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  fireEvent.click(screen.getByRole('button', { name: /ampliar foto 1/i }))
  fireEvent.click(screen.getByRole('button', { name: 'Próxima foto' }))
  expect(screen.getByAltText('Foto ampliada da sala Sala Alfa')).toHaveAttribute('src', photos[1])
  fireEvent.click(screen.getByRole('button', { name: 'Foto anterior' }))
  expect(screen.getByAltText('Foto ampliada da sala Sala Alfa')).toHaveAttribute('src', photos[0])
  fireEvent.click(screen.getByRole('button', { name: 'Foto anterior' }))
  expect(screen.getByAltText('Foto ampliada da sala Sala Alfa')).toHaveAttribute('src', photos[2])
})

test('the lightbox arrow keys also navigate between photos', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  fireEvent.click(screen.getByRole('button', { name: /ampliar foto 1/i }))
  fireEvent.keyDown(document, { key: 'ArrowRight' })
  expect(screen.getByAltText('Foto ampliada da sala Sala Alfa')).toHaveAttribute('src', photos[1])
  fireEvent.keyDown(document, { key: 'ArrowLeft' })
  expect(screen.getByAltText('Foto ampliada da sala Sala Alfa')).toHaveAttribute('src', photos[0])
})

test('with a single photo the lightbox omits navigation arrows but still opens and closes', () => {
  render(<RoomPhotoGallery photoUrls={[photos[0]]} roomName="Sala Alfa" />)
  fireEvent.click(screen.getByRole('button', { name: /ampliar foto 1/i }))
  expect(screen.getByRole('dialog')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Próxima foto' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Foto anterior' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Fechar' }))
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})

test('a main photo load failure swaps it for the fallback without losing the thumbnail strip', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  fireEvent.error(screen.getByAltText('Foto da sala Sala Alfa'))
  expect(screen.getByTestId('totem-room-detail-fallback')).toBeInTheDocument()
  expect(screen.getAllByRole('button', { name: /ver foto/i })).toHaveLength(3)
})

test('a thumbnail load failure swaps just that thumbnail for a fallback', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  const thumbs = screen.getAllByRole('button', { name: /ver foto/i })
  fireEvent.error(thumbs[1].querySelector('img')!)
  expect(screen.getByTestId('totem-room-detail-thumb-fallback')).toBeInTheDocument()
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toBeInTheDocument()
})

test('a broken photo shows the fallback icon inside the lightbox too', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  fireEvent.error(screen.getByAltText('Foto da sala Sala Alfa'))
  fireEvent.click(screen.getByRole('button', { name: /ampliar foto 1/i }))
  expect(screen.getByTestId('totem-room-detail-lightbox-fallback')).toBeInTheDocument()
})

test('autoplay advances to the next photo automatically every 5 seconds', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toHaveAttribute('src', photos[0])
  advance(5000)
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toHaveAttribute('src', photos[1])
  advance(5000)
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toHaveAttribute('src', photos[2])
  advance(5000)
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toHaveAttribute('src', photos[0])
})

test('autoplay never starts with a single photo', () => {
  render(<RoomPhotoGallery photoUrls={[photos[0]]} roomName="Sala Alfa" />)
  advance(20000)
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toHaveAttribute('src', photos[0])
})

test('autoplay stops permanently after a manual thumbnail click', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  const thumbs = screen.getAllByRole('button', { name: /ver foto/i })
  fireEvent.click(thumbs[1])
  fireEvent.click(screen.getByRole('button', { name: 'Fechar' }))
  advance(20000)
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toHaveAttribute('src', photos[1])
})

test('autoplay stops permanently after opening the lightbox from the main photo', () => {
  render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  fireEvent.click(screen.getByRole('button', { name: /ampliar foto 1/i }))
  fireEvent.click(screen.getByRole('button', { name: 'Fechar' }))
  advance(20000)
  expect(screen.getByAltText('Foto da sala Sala Alfa')).toHaveAttribute('src', photos[0])
})

test('the autoplay timer is cleared on unmount', () => {
  const { unmount } = render(<RoomPhotoGallery photoUrls={photos} roomName="Sala Alfa" />)
  const clearSpy = vi.spyOn(globalThis, 'clearInterval')
  unmount()
  expect(clearSpy).toHaveBeenCalled()
  clearSpy.mockRestore()
})
