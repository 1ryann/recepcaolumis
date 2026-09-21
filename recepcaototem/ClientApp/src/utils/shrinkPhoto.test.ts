import { expect, test, vi } from 'vitest'
import { type PhotoEncoder, photoUploadMaxBytes, shrinkPhotoForUpload } from './shrinkPhoto'

const MB = 1024 * 1024
const fileOf = (bytes: number, name = 'sala.png', type = 'image/png') => new File([new Uint8Array(bytes)], name, { type })
const blobOf = (bytes: number) => new Blob([new Uint8Array(bytes)], { type: 'image/jpeg' })

test('matches the server limit for room photos (Storage:RoomPhotoMaxBytes)', () => {
  expect(photoUploadMaxBytes).toBe(5 * MB)
})

test('a photo within the limit is sent untouched, without re-encoding', async () => {
  const encode = vi.fn<PhotoEncoder>()
  const file = fileOf(photoUploadMaxBytes)
  expect(await shrinkPhotoForUpload(file, encode)).toBe(file)
  expect(encode).not.toHaveBeenCalled()
})

test('a photo above the limit is re-encoded as a JPEG whose name matches its type', async () => {
  const encode = vi.fn<PhotoEncoder>().mockResolvedValue(blobOf(2 * MB))
  const result = await shrinkPhotoForUpload(fileOf(12 * MB, 'IMG_2031.PNG'), encode)
  expect(encode).toHaveBeenCalledWith(expect.any(File), 2560, 0.85)
  expect(result.name).toBe('IMG_2031.jpg')
  expect(result.type).toBe('image/jpeg')
  expect(result.size).toBe(2 * MB)
})

test('steps down size and quality until the photo fits', async () => {
  const encode = vi.fn<PhotoEncoder>()
    .mockResolvedValueOnce(blobOf(7 * MB))
    .mockResolvedValueOnce(blobOf(6 * MB))
    .mockResolvedValueOnce(blobOf(3 * MB))
  const result = await shrinkPhotoForUpload(fileOf(20 * MB, 'sala.jpeg', 'image/jpeg'), encode)
  expect(encode.mock.calls.map(([, size, quality]) => [size, quality])).toEqual([[2560, 0.85], [2048, 0.8], [1600, 0.75]])
  expect(result.size).toBe(3 * MB)
})

test('explains the limit when no attempt gets under it', async () => {
  const encode = vi.fn<PhotoEncoder>().mockResolvedValue(blobOf(6 * MB))
  await expect(shrinkPhotoForUpload(fileOf(30 * MB), encode)).rejects.toThrow('A foto é grande demais. Envie uma imagem de até 5 MB.')
})

test('explains the accepted formats when the image cannot be decoded', async () => {
  const encode = vi.fn<PhotoEncoder>().mockRejectedValue(new Error('decode failed'))
  await expect(shrinkPhotoForUpload(fileOf(8 * MB), encode)).rejects.toThrow('Não foi possível processar a foto. Envie uma imagem JPG, PNG ou WebP.')
})
