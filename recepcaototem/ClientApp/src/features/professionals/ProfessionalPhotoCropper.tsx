import Cropper, { type Area } from 'react-easy-crop'
import { useEffect, useRef, useState } from 'react'
import { Modal } from '../../components/Modal'
import { getCroppedImageBlob } from './cropToBlob'

export function ProfessionalPhotoCropper({ file, onCancel, onCropped, onUploadError }: {
  file: File; onCancel: () => void; onCropped: (blob: Blob) => void | Promise<void>; onUploadError: (message: string) => void
}) {
  const objectUrlRef = useRef<string>('')
  if (!objectUrlRef.current) objectUrlRef.current = URL.createObjectURL(file)
  useEffect(() => () => URL.revokeObjectURL(objectUrlRef.current), [])

  const [crop, setCrop] = useState({ x: 0, y: 0 })
  const [zoom, setZoom] = useState(1)
  const [pixelCrop, setPixelCrop] = useState<Area | null>(null)
  const [saving, setSaving] = useState(false)

  const save = async () => {
    if (!pixelCrop) return
    setSaving(true)
    try {
      const blob = await getCroppedImageBlob(objectUrlRef.current, pixelCrop)
      await onCropped(blob)
    } catch (error) {
      onUploadError(error instanceof Error ? error.message : 'Não foi possível salvar a foto.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open title="Ajustar foto" onClose={onCancel}>
      <div className="professional-photo-crop-stage">
        <Cropper
          image={objectUrlRef.current} crop={crop} zoom={zoom} aspect={1} cropShape="round" showGrid={false}
          onCropChange={setCrop} onZoomChange={setZoom}
          onCropComplete={(_area, areaPixels) => setPixelCrop(areaPixels)}
        />
      </div>
      <label className="field-label">Zoom
        <input type="range" aria-label="Zoom" min={1} max={3} step={0.1} value={zoom}
          onChange={(event) => setZoom(Number(event.target.value))} />
      </label>
      <div className="modal-actions">
        <button className="ghost-button" type="button" onClick={onCancel} disabled={saving}>Cancelar</button>
        <button className="primary-button" type="button" onClick={() => void save()} disabled={saving || !pixelCrop}>
          {saving ? 'Salvando…' : 'Salvar foto'}
        </button>
      </div>
    </Modal>
  )
}
