// Every room photo is re-encoded in the browser as a plain JPEG before upload. The server's
// validator rejects (as "foto inválida") what cameras routinely produce:
//  - files over 5 MB (Storage:RoomPhotoMaxBytes);
//  - sides over 4096px — 24 MP iPhone photos are 5712×4284;
//  - data after the first JPEG end marker — iPhone MPF/HDR gain-map images are appended there.
// Size alone can't predict the last two (IMG_4338.JPG was 4.99 MB and still rejected), so the
// photo is always redrawn: longest side ≤ 2560px, nothing but the pixels. The server normalises
// every photo to a 1920px WebP anyway, so nothing a catalogue viewer would notice is lost.
export const photoUploadMaxBytes = 5 * 1024 * 1024

export type PhotoEncoder = (file: File, maxDimension: number, quality: number) => Promise<Blob>

const attempts: ReadonlyArray<readonly [maxDimension: number, quality: number]> = [[2560, 0.85], [2048, 0.8], [1600, 0.75]]

export async function shrinkPhotoForUpload(file: File, encode: PhotoEncoder = encodeInBrowser): Promise<File> {
  // The server requires the extension to match the content, so the new file is always *.jpg.
  const name = `${file.name.replace(/\.[^.]*$/, '') || 'foto'}.jpg`
  for (const [maxDimension, quality] of attempts) {
    let blob: Blob
    try { blob = await encode(file, maxDimension, quality) }
    catch {
      // Undecodable here: a small file may still be something the server accepts, so let it judge.
      if (file.size <= photoUploadMaxBytes) return file
      throw new Error('Não foi possível processar a foto. Envie uma imagem JPG, PNG ou WebP.')
    }
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
