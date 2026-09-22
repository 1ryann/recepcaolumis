import { FallbackImage } from '../../components/FallbackImage'
import { ImagePlus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { ProfessionalDto } from '../../api/modules'

const accepted = '.jpg,.jpeg,.png,.webp'

export function ProfessionalPhotoEditor({
  professional,
  pending,
  onClose,
  onUpload,
  onRemove,
}: {
  professional: ProfessionalDto
  pending: boolean
  onClose(): void
  onUpload(file: File): Promise<void>
  onRemove(): Promise<void>
}) {
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview) }, [preview])
  const select = (next?: File) => {
    if (!next) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(next.type)) {
      setError('Selecione uma imagem JPEG, PNG ou WebP.'); return
    }
    if (preview) URL.revokeObjectURL(preview)
    setFile(next); setPreview(URL.createObjectURL(next)); setError(null)
  }
  const upload = async () => {
    if (!file) { setError('Selecione uma foto para enviar.'); return }
    try { await onUpload(file); onClose() } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível enviar a foto.')
    }
  }
  const remove = async () => {
    try { await onRemove(); onClose() } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível remover a foto.')
    }
  }

  return <div className="photo-editor">
    <div className="photo-upload"><div>
      {preview ? <img src={preview} alt="Prévia da foto" />
        : professional.hasPhoto && professional.photoUrl ? <FallbackImage src={professional.photoUrl} alt={`Foto de ${professional.name}`} fallback={<span>{professional.name.slice(0, 1).toUpperCase()}</span>} />
          : <span>{professional.name.slice(0, 1).toUpperCase()}</span>}
    </div>
      <label className="secondary-button"><ImagePlus size={16} /> Escolher foto
        <input type="file" accept={accepted} onChange={event => select(event.target.files?.[0])} />
      </label>
      <small>JPEG, PNG ou WebP, até 5 MB.</small>
    </div>
    {error && <p className="form-error" role="alert">{error}</p>}
    <div className="modal-actions">
      <button className="ghost-button" type="button" onClick={onClose} disabled={pending}>Cancelar</button>
      {professional.hasPhoto && <button className="danger-button" type="button" onClick={() => void remove()} disabled={pending}>
        <Trash2 size={16} /> Remover
      </button>}
      <button className="primary-button" type="button" onClick={() => void upload()} disabled={pending || !file}>
        {pending ? 'Enviando…' : 'Salvar foto'}
      </button>
    </div>
  </div>
}
