import { ChevronDown, ChevronUp, ImagePlus, Star } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { type RoomDto, type RoomPhotoDto, roomPhotosApi } from '../../api/modules'
import { EmptyState } from '../../components/PageElements'
import { shrinkPhotoForUpload } from '../../utils/shrinkPhoto'

const accepted = 'image/jpeg,image/png,image/webp'
const maxPhotos = 8

function errorMessage(reason: unknown, fallback: string) {
  return reason instanceof Error ? reason.message : fallback
}

// Photos are managed one mutation at a time (upload/remove/reorder/cover), each followed by a
// full reload from the server — no manual patching of local state. That keeps this in lockstep
// with the backend's own reordering/cover invariants (e.g. removing the cover promotes the new
// first photo) instead of guessing them client-side.
// `onClose` is part of the contract for parity with other modal-hosted editors, but closing is
// already handled by the surrounding Modal (header close button, backdrop click, Escape) — this
// manager never needs to close itself after a mutation, so the prop is accepted and unused here.
export function RoomPhotoManager({ room, onClose: _onClose }: { room: RoomDto; onClose: () => void }) {
  const [photos, setPhotos] = useState<RoomPhotoDto[] | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [confirmId, setConfirmId] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setError(null)
      const data = await roomPhotosApi.list(room.id)
      setPhotos([...data].sort((a, b) => a.sortOrder - b.sortOrder))
    } catch (reason) {
      setError(errorMessage(reason, 'Não foi possível carregar as fotos.'))
    }
  }, [room.id])

  const reload = useCallback(() => {
    setLoading(true)
    void load().finally(() => setLoading(false))
  }, [load])

  useEffect(() => { reload() }, [reload])

  const runMutation = useCallback(async (action: () => Promise<unknown>, fallback: string) => {
    setBusy(true)
    try {
      await action()
      await load()
    } catch (reason) {
      setError(errorMessage(reason, fallback))
    } finally {
      setBusy(false)
    }
  }, [load])

  const upload = (file: File | undefined, input: HTMLInputElement) => {
    if (!file) return
    void runMutation(async () => roomPhotosApi.upload(room.id, await shrinkPhotoForUpload(file)), 'Não foi possível enviar a foto.')
      .finally(() => { input.value = '' })
  }
  const remove = (photoId: string) => {
    void runMutation(() => roomPhotosApi.remove(room.id, photoId), 'Não foi possível remover a foto.')
      .finally(() => setConfirmId(null))
  }
  const setCover = (photoId: string) => {
    void runMutation(() => roomPhotosApi.setCover(room.id, photoId), 'Não foi possível definir a capa.')
  }
  const move = (index: number, direction: -1 | 1) => {
    if (!photos) return
    const target = index + direction
    if (target < 0 || target >= photos.length) return
    const next = [...photos]
    ;[next[index], next[target]] = [next[target], next[index]]
    void runMutation(() => roomPhotosApi.reorder(room.id, next.map(item => item.id)), 'Não foi possível reordenar as fotos.')
  }

  const count = photos?.length ?? 0
  const atLimit = count >= maxPhotos

  return <div className="room-photo-manager">
    {loading ? <div className="empty-state" role="status">Carregando fotos…</div>
      : error && !photos ? <EmptyState><p>{error}</p><button className="secondary-button" type="button" onClick={reload}>Tentar novamente</button></EmptyState>
        : <>
          <div className="room-photo-toolbar">
            <span className="room-photo-counter">{count}/{maxPhotos} fotos</span>
            <label className="secondary-button">
              <ImagePlus size={16} /> Adicionar foto
              <input type="file" accept={accepted} disabled={busy || atLimit}
                onChange={event => upload(event.target.files?.[0], event.target)} />
            </label>
          </div>
          {atLimit && <p className="room-photo-hint">Limite de 8 fotos atingido. Remova uma foto para enviar outra.</p>}
          {error && <p className="form-error" role="alert">{error}</p>}
          {photos && photos.length === 0
            ? <EmptyState>Nenhuma foto cadastrada.</EmptyState>
            : <div className="room-photo-grid">
              {(photos ?? []).map((item, index) => <article className="room-photo-card" key={item.id}>
                <div className="room-photo-frame">
                  <img src={item.photoUrl} alt={`Foto ${index + 1} da sala ${room.name}`} />
                  {item.isCover && <span className="room-photo-badge"><Star size={12} /> Capa</span>}
                </div>
                <div className="room-photo-actions">
                  <button type="button" className="icon-button" disabled={busy || index === 0}
                    aria-label={`Mover foto ${index + 1} para antes`} onClick={() => move(index, -1)}>
                    <ChevronUp size={16} />
                  </button>
                  <button type="button" className="icon-button" disabled={busy || index === count - 1}
                    aria-label={`Mover foto ${index + 1} para depois`} onClick={() => move(index, 1)}>
                    <ChevronDown size={16} />
                  </button>
                  {!item.isCover && <button type="button" className="secondary-button" disabled={busy}
                    onClick={() => setCover(item.id)}>{`Definir foto ${index + 1} como capa`}</button>}
                  {confirmId === item.id
                    ? <span className="room-photo-confirm">
                      <button type="button" className="ghost-button" disabled={busy}
                        aria-label={`Cancelar remoção da foto ${index + 1}`} onClick={() => setConfirmId(null)}>Cancelar</button>
                      <button type="button" className="danger-button" disabled={busy}
                        aria-label={`Confirmar remoção da foto ${index + 1}`} onClick={() => remove(item.id)}>Remover</button>
                    </span>
                    : <button type="button" className="danger-button" disabled={busy}
                      aria-label={`Remover foto ${index + 1}`} onClick={() => setConfirmId(item.id)}>Remover</button>}
                </div>
              </article>)}
            </div>}
        </>}
  </div>
}
