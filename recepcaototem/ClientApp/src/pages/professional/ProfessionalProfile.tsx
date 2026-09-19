import { Camera, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { professionalProfileApi, whatsAppOptInApi, type ProfessionalProfileDto } from '../../api/modules'
import { WhatsAppOptInPanel } from '../../features/whatsapp/WhatsAppOptIn'
import { PROFESSIONAL_OPT_IN_TEXT } from '../../features/whatsapp/optInText'
import { PageHeader } from '../../components/PageElements'
import { ProfessionalPhotoCropper } from '../../features/professionals/ProfessionalPhotoCropper'

const accepted = 'image/png,image/jpeg,image/webp'

function initials(name: string) {
  const trimmed = name.trim()
  if (!trimmed) return '?'
  const parts = trimmed.split(/\s+/)
  return parts.length === 1 ? parts[0].slice(0, 1).toUpperCase() : `${parts[0].slice(0, 1)}${parts[parts.length - 1].slice(0, 1)}`.toUpperCase()
}

const errorMessage = (error: unknown) => {
  if (error instanceof ApiError) {
    if (error.code === 'INVALID_PROFESSIONAL_PHOTO') return 'Não foi possível processar a foto enviada. Selecione outra imagem.'
    if (error.code === 'PROFESSIONAL_PROFILE_NOT_LINKED') return 'Sua conta ainda não está vinculada a um perfil profissional.'
    if (error.status === 429) return 'Muitas tentativas em sequência. Aguarde um instante e tente novamente.'
    return error.message
  }
  return 'Não foi possível carregar seu perfil agora.'
}

export function ProfessionalProfile() {
  const [profile, setProfile] = useState<ProfessionalProfileDto | null>(null)
  const [whatsApp, setWhatsApp] = useState('')
  const [description, setDescription] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState('')
  const [pendingFile, setPendingFile] = useState<File | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const applyProfile = (next: ProfessionalProfileDto) => {
    setProfile(next); setWhatsApp(next.whatsApp); setDescription(next.description ?? '')
  }

  const load = useCallback(async (signal?: AbortSignal) => {
    const next = await professionalProfileApi.get(signal)
    applyProfile(next)
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    setError('')
    void load(controller.signal).catch((reason) => { if (!controller.signal.aborted) setError(errorMessage(reason)) }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [load])

  const reloadAfterConflict = async () => {
    try { await load(); setError('Este perfil foi alterado em outra sessão. Atualizamos os dados para você.') }
    catch (reason) { setError(errorMessage(reason)) }
  }

  const dirty = profile ? whatsApp !== profile.whatsApp || description !== (profile.description ?? '') : false

  const save = async () => {
    if (!profile) return
    setSaving(true); setError('')
    try {
      applyProfile(await professionalProfileApi.update({ whatsApp, description: description.trim() || null, concurrencyToken: profile.concurrencyToken }))
    } catch (reason) {
      if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') await reloadAfterConflict()
      else setError(errorMessage(reason))
    } finally { setSaving(false) }
  }

  const selectPhoto = (file?: File) => {
    if (!file) return
    setPendingFile(file)
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  const cropped = async (blob: Blob) => {
    if (!profile) return
    setUploading(true); setError('')
    try {
      const response = await professionalProfileApi.uploadPhoto(blob, profile.concurrencyToken)
      setProfile((current) => current ? { ...current, ...response } : current)
      setPendingFile(null)
    } catch (reason) {
      if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') { await reloadAfterConflict(); setPendingFile(null) }
      else throw reason
    } finally { setUploading(false) }
  }

  const removePhoto = async () => {
    if (!profile) return
    setUploading(true); setError('')
    try {
      const response = await professionalProfileApi.deletePhoto(profile.concurrencyToken)
      setProfile((current) => current ? { ...current, ...response } : current)
    } catch (reason) {
      if (reason instanceof ApiError && reason.code === 'RESOURCE_MODIFIED') await reloadAfterConflict()
      else setError(errorMessage(reason))
    } finally { setUploading(false) }
  }

  return (
    <section className="professional-section page-enter professional-profile-page">
      <PageHeader eyebrow="Área do profissional" title="Meu perfil" description="Atualize sua foto, WhatsApp e descrição visíveis para os clientes." />
      {error && <div className="form-error" role="alert">{error}</div>}
      {loading ? <div className="professional-loading" role="status">Carregando perfil…</div> : profile && (
        <div className="panel professional-profile-panel">
          <div className="professional-profile-photo">
            <div className="professional-profile-photo-frame">
              {profile.hasPhoto && profile.photoUrl
                ? <img src={`${profile.photoUrl}?v=${profile.concurrencyToken}`} alt={`Foto de ${profile.name}`} />
                : <span aria-hidden="true">{initials(profile.name)}</span>}
            </div>
            <label className="secondary-button">
              <Camera size={16} /> Trocar foto
              <input ref={fileInputRef} type="file" accept={accepted} disabled={uploading}
                onChange={(event) => selectPhoto(event.target.files?.[0])} />
            </label>
            {profile.hasPhoto && (
              <button className="danger-button" type="button" disabled={uploading} onClick={() => void removePhoto()}>
                <Trash2 size={16} /> Remover foto
              </button>
            )}
          </div>
          <div className="professional-profile-facts">
            <div><span className="field-label">Nome</span><p>{profile.name}</p></div>
            <div><span className="field-label">Profissão</span><p>{profile.profession}</p></div>
          </div>
          <form className="professional-profile-form" onSubmit={(event) => { event.preventDefault(); void save() }}>
            <label className="field-label">WhatsApp
              <input className="field-input" aria-label="WhatsApp" value={whatsApp} onChange={(event) => setWhatsApp(event.target.value)} />
              {whatsApp !== profile.whatsApp && <small className="field-hint">Ao trocar o número, os avisos por WhatsApp precisam ser autorizados de novo para o número novo.</small>}
            </label>
            <label className="field-label">Descrição
              <textarea className="field-input field-textarea" aria-label="Descrição" value={description} onChange={(event) => setDescription(event.target.value)} />
            </label>
            <div className="modal-actions">
              <button className="primary-button" type="submit" disabled={saving || !dirty}>
                {saving ? 'Salvando…' : 'Salvar'}
              </button>
            </div>
          </form>
        </div>
      )}
      {!loading && profile && (
        <div className="panel">
          <div className="panel-header"><div><h2>Avisos por WhatsApp</h2><p>Por exemplo, quando um cliente chega para o atendimento.</p></div></div>
          {/* Keyed by the number: a new number starts without an opt-in, so the panel reloads after it changes. */}
          <WhatsAppOptInPanel key={profile.whatsApp} text={PROFESSIONAL_OPT_IN_TEXT}
            load={whatsAppOptInApi.professional} save={whatsAppOptInApi.setProfessional} />
        </div>
      )}
      {pendingFile && (
        <ProfessionalPhotoCropper
          file={pendingFile}
          onCancel={() => setPendingFile(null)}
          onCropped={cropped}
          onUploadError={(message) => setError(message)}
        />
      )}
    </section>
  )
}
