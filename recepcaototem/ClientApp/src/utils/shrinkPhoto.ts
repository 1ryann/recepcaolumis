// Room photos are capped at 5 MB by the server (Storage:RoomPhotoMaxBytes), but phone photos are
// usually 5–12 MB and were rejected as "foto inválida". Oversized photos are re-encoded in the
// browser as JPEG before upload; the server normalises every photo again anyway, so nothing a
// catalogue viewer would notice is lost. Photos already within the limit are sent untouched.
export const photoUploadMaxBytes = 5 * 1024 * 1024

export type PhotoEncoder = (file: File, maxDimension: number, quality: number) => Promise<Blob>

const attempts: ReadonlyArray<readonly [maxDimension: number, quality: number]> = [[2560, 0.85], [2048, 0.8], [1600, 0.75]]

export async function shrinkPhotoForUpload(file: File, encode: PhotoEncoder = encodeInBrowser): Promise<File> {
  if (file.size <= photoUploadMaxBytes) return file
  // The server requires the extension to match the content, so the new file is always *.jpg.
  const name = `${file.name.replace(/\.[^.]*$/, '') || 'foto'}.jpg`
  for (const [maxDimension, quality] of attempts) {
    let blob: Blob
    try { blob = await encode(file, maxDimension, quality) }
    catch { throw new Error('Não foi possível processar a foto. Envie uma imagem JPG, PNG ou WebP.') }
    if (blob.size <= photoUploadMaxBytes) return new File([blob], name, { type: 'image/jpeg' })
  }
  throw new Error('A foto é grande demais. Envie uma imagem de até 5 MB.')
}

async function encodeInBrowser(file: File, maxDimension: number, quality: number): Promise<Blob> {
  // `from-image` applies the EXIF rotation, so portrait phone photos don't come out sideways.
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  try {
    const scale = Math.min(1, maxDimension / Math.max(bitmap.width, bitmap.height))
    const canvas = document.createElement('canvas')
    canvas.width = Math.round(bitmap.width * scale)
    canvas.height = Math.round(bitmap.height * scale)
    const context = canvas.getContext('2d')
    if (!context) throw new Error('Canvas 2D indisponível.')
    // JPEG has no transparency: paint white first so transparent PNG areas don't turn black.
    context.fillStyle = '#fff'
    context.fillRect(0, 0, canvas.width, canvas.height)
    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
    return await new Promise<Blob>((resolve, reject) =>
      canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('Falha ao gerar JPEG.')), 'image/jpeg', quality))
  } finally {
    bitmap.close()
  }
}
