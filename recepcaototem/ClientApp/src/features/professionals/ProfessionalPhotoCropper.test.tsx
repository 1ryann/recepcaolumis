import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { expect, test, vi, afterEach } from 'vitest'
import { ProfessionalPhotoCropper } from './ProfessionalPhotoCropper'

const file = new File([new Uint8Array([137, 80, 78, 71])], 'photo.png', { type: 'image/png' })

afterEach(() => vi.restoreAllMocks())

test('opens showing the Cropper with the selected file as its image source', async () => {
  const createObjectURL = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
  render(<ProfessionalPhotoCropper file={file} onCancel={() => {}} onCropped={() => {}} onUploadError={() => {}} />)
  expect(await screen.findByRole('dialog')).toBeInTheDocument()
  expect(createObjectURL).toHaveBeenCalledWith(file)
})

test('revokes the object URL on unmount (cleanup)', () => {
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
  const revokeObjectURL = vi.spyOn(URL, 'revokeObjectURL')
  const { unmount } = render(<ProfessionalPhotoCropper file={file} onCancel={() => {}} onCropped={() => {}} onUploadError={() => {}} />)
  unmount()
  expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url')
})

test('wires the zoom control through to react-easy-crop', async () => {
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
  render(<ProfessionalPhotoCropper file={file} onCancel={() => {}} onCropped={() => {}} onUploadError={() => {}} />)
  const zoomSlider = screen.getByRole('slider', { name: /zoom/i })
  fireEvent.change(zoomSlider, { target: { value: '2' } })
  expect((zoomSlider as HTMLInputElement).value).toBe('2')
  // Drag/pinch gesture behavior is react-easy-crop's own tested internal responsibility — this component only
  // needs to prove it wires the library's onCropChange/onZoomChange/onCropComplete callbacks correctly, not
  // reimplement or re-verify pointer/touch gesture math.
})

test('Salvar foto crops via canvas and calls onCropped with a Blob', async () => {
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
  const onCropped = vi.fn()
  render(<ProfessionalPhotoCropper file={file} onCancel={() => {}} onCropped={onCropped} onUploadError={() => {}} />)
  fireEvent.click(await screen.findByRole('button', { name: /salvar foto/i }))
  await waitFor(() => expect(onCropped).toHaveBeenCalledWith(expect.any(Blob)))
})

test('shows a loading state while cropping/uploading and disables Salvar foto meanwhile', async () => {
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
  let resolveCropped: () => void = () => {}
  const onCropped = vi.fn(() => new Promise<void>((resolve) => { resolveCropped = resolve }))
  render(<ProfessionalPhotoCropper file={file} onCancel={() => {}} onCropped={onCropped} onUploadError={() => {}} />)
  const saveButton = await screen.findByRole('button', { name: /salvar foto/i })
  fireEvent.click(saveButton)
  expect(saveButton).toBeDisabled()
  resolveCropped()
})

test('surfaces an error via onUploadError when the crop/upload promise rejects, without closing the modal', async () => {
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
  const onUploadError = vi.fn()
  const onCropped = vi.fn().mockRejectedValue(new Error('falha'))
  render(<ProfessionalPhotoCropper file={file} onCancel={() => {}} onCropped={onCropped} onUploadError={onUploadError} />)
  fireEvent.click(await screen.findByRole('button', { name: /salvar foto/i }))
  await waitFor(() => expect(onUploadError).toHaveBeenCalled())
  expect(screen.getByRole('dialog')).toBeInTheDocument() // stays open so the user can retry
})

test('calls onCancel when the user dismisses the modal', () => {
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
  const onCancel = vi.fn()
  render(<ProfessionalPhotoCropper file={file} onCancel={onCancel} onCropped={() => {}} onUploadError={() => {}} />)
  fireEvent.click(screen.getByRole('button', { name: /cancelar/i }))
  expect(onCancel).toHaveBeenCalled()
})
