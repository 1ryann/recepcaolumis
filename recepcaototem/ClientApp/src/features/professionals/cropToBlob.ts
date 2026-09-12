export interface PixelCrop { x: number; y: number; width: number; height: number }

export function getCroppedImageBlob(imageSrc: string, crop: PixelCrop): Promise<Blob> {
  return new Promise((resolve, reject) => {
    const image = new Image()
    image.onload = () => {
      const canvas = document.createElement('canvas')
      canvas.width = crop.width
      canvas.height = crop.height
      const ctx = canvas.getContext('2d')
      if (!ctx) { reject(new Error('Canvas indisponível.')); return }
      ctx.drawImage(image, crop.x, crop.y, crop.width, crop.height, 0, 0, crop.width, crop.height)
      canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Falha ao gerar a imagem recortada.'))), 'image/png')
    }
    image.onerror = () => reject(new Error('Não foi possível carregar a imagem selecionada.'))
    image.src = imageSrc
  })
}
