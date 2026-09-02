import { ArrowLeft, ArrowRight, BriefcaseBusiness, Camera, Check, DoorOpen, MoveHorizontal, Phone, RefreshCw, Search, ShieldCheck, UserRound } from 'lucide-react'
import { type FormEvent, type PointerEvent, useCallback, useEffect, useRef, useState } from 'react'
import type { Professional } from '../data/mock'
import { useAppStore } from '../store/AppStore'
import { Modal } from '../components/Modal'

type Step = 'select' | 'identify' | 'camera' | 'success'

export function Reception() {
  const { professionals, addVisit } = useAppStore()
  const [step, setStep] = useState<Step>('select')
  const [selected, setSelected] = useState<Professional | null>(null)
  const [visitorName, setVisitorName] = useState('')
  const [photo, setPhoto] = useState('')
  const [cameraError, setCameraError] = useState('')
  const [rentalOpen, setRentalOpen] = useState(false)
  const [rentalSuccess, setRentalSuccess] = useState(false)
  const [rentalForm, setRentalForm] = useState({ name: '', profession: '', phone: '' })
  const [professionalQuery, setProfessionalQuery] = useState('')
  const [galleryFocusId, setGalleryFocusId] = useState('beatriz')
  const [galleryDragX, setGalleryDragX] = useState(0)
  const [galleryDragging, setGalleryDragging] = useState(false)
  const videoRef = useRef<HTMLVideoElement>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const galleryRef = useRef<HTMLDivElement>(null)
  const galleryDragStartRef = useRef(0)
  const galleryDidDragRef = useRef(false)

  const stopCamera = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => track.stop())
    streamRef.current = null
  }, [])

  useEffect(() => {
    if (step !== 'camera' || photo) return
    let active = true
    navigator.mediaDevices?.getUserMedia({ video: { facingMode: 'user' }, audio: false })
      .then((stream) => {
        if (!active) return stream.getTracks().forEach((track) => track.stop())
        streamRef.current = stream
        if (videoRef.current) videoRef.current.srcObject = stream
      })
      .catch(() => setCameraError('Não foi possível acessar a câmera. Você ainda pode confirmar a chegada.'))
    return () => { active = false; stopCamera() }
  }, [step, photo, stopCamera])

  useEffect(() => {
    if (step !== 'success') return
    const timer = window.setTimeout(() => reset(), 6500)
    return () => window.clearTimeout(timer)
  }, [step])

  useEffect(() => {
    if (step !== 'select' || window.innerWidth > 820) return
    const frame = window.requestAnimationFrame(() => {
      const stage = galleryRef.current
      const focused = stage?.querySelector<HTMLElement>('.lumis-gallery-card.is-focused')
      if (stage && focused) stage.scrollTo({ left: focused.offsetLeft - (stage.clientWidth - focused.offsetWidth) / 2, behavior: 'smooth' })
    })
    return () => window.cancelAnimationFrame(frame)
  }, [galleryFocusId, professionalQuery, step])

  const reset = () => { stopCamera(); setStep('select'); setSelected(null); setVisitorName(''); setPhoto(''); setCameraError('') }
  const choose = (professional: Professional) => { setSelected(professional); setStep('identify') }
  const continueToCamera = (event: FormEvent) => { event.preventDefault(); if (visitorName.trim().length >= 2) setStep('camera') }
  const takePhoto = () => {
    const video = videoRef.current
    if (!video || !video.videoWidth) return setCameraError('Aguarde um instante enquanto a câmera é preparada.')
    const canvas = document.createElement('canvas')
    canvas.width = video.videoWidth; canvas.height = video.videoHeight
    canvas.getContext('2d')?.drawImage(video, 0, 0)
    setPhoto(canvas.toDataURL('image/jpeg', .78)); stopCamera()
  }
  const confirm = () => {
    if (!selected) return
    const now = new Date()
    addVisit({ date: now.toISOString().slice(0, 10), time: now.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }), visitor: visitorName.trim(), professionalId: selected.id, room: selected.room })
    stopCamera(); setStep('success')
  }
  const submitRentalInterest = (event: FormEvent) => {
    event.preventDefault()
    const current = JSON.parse(localStorage.getItem('atrium_rental_interests') ?? '[]') as object[]
    localStorage.setItem('atrium_rental_interests', JSON.stringify([{ ...rentalForm, createdAt: new Date().toISOString() }, ...current]))
    setRentalSuccess(true)
  }
  const closeRental = () => { setRentalOpen(false); setRentalSuccess(false); setRentalForm({ name: '', profession: '', phone: '' }) }
  const visibleProfessionals = professionals.filter((item) => item.active && `${item.name} ${item.profession} ${item.room}`.toLowerCase().includes(professionalQuery.toLowerCase()))
  const galleryFocus = visibleProfessionals.find((item) => item.id === galleryFocusId) ?? visibleProfessionals[0]
  const galleryFocusIndex = Math.max(0, visibleProfessionals.findIndex((item) => item.id === galleryFocus?.id))
  const galleryProfessionals = visibleProfessionals.length > 1
    ? Array.from({ length: visibleProfessionals.length }, (_, index) => visibleProfessionals[(galleryFocusIndex - Math.floor(visibleProfessionals.length / 2) + index + visibleProfessionals.length) % visibleProfessionals.length])
    : visibleProfessionals
  const receptionPhoto = (professional: Professional) => ({
    ana: '/dra-ana-lumis.png', carlos: '/carlos-lumis.jpg', beatriz: '/beatriz-lumis.jpg', rafael: '/rafael-lumis.jpg', marina: '/marina-lumis.jpg',
  }[professional.id] ?? professional.photo)
  const handleGalleryProfessional = (professional: Professional) => {
    if (galleryDidDragRef.current) { galleryDidDragRef.current = false; return }
    if (professional.id === galleryFocus?.id || visibleProfessionals.length === 1) choose(professional)
    else setGalleryFocusId(professional.id)
  }
  const shiftGallery = (direction: number) => {
    if (!galleryFocus || visibleProfessionals.length < 2) return
    const currentIndex = visibleProfessionals.findIndex((item) => item.id === galleryFocus.id)
    const nextIndex = (currentIndex + direction + visibleProfessionals.length) % visibleProfessionals.length
    setGalleryFocusId(visibleProfessionals[nextIndex].id)
  }
  const startGalleryDrag = (event: PointerEvent<HTMLDivElement>) => {
    // On touch devices the horizontal stage uses the browser's native scroll-snap.
    // Keeping the custom drag for mouse input prevents a swipe from racing a card click.
    if (event.pointerType !== 'mouse') return
    galleryDragStartRef.current = event.clientX
    galleryDidDragRef.current = false
    setGalleryDragging(true)
    event.currentTarget.setPointerCapture(event.pointerId)
  }
  const moveGalleryDrag = (event: PointerEvent<HTMLDivElement>) => {
    if (event.pointerType !== 'mouse' || !galleryDragging) return
    const delta = event.clientX - galleryDragStartRef.current
    // Ignore only intentional movement; tiny pointer jitter should never block a tap/click.
    if (Math.abs(delta) > 18) galleryDidDragRef.current = true
    setGalleryDragX(Math.max(-88, Math.min(88, delta * .42)))
  }
  const finishGalleryDrag = (event: PointerEvent<HTMLDivElement>) => {
    if (event.pointerType !== 'mouse' || !galleryDragging) return
    const delta = event.clientX - galleryDragStartRef.current
    if (Math.abs(delta) > 48) shiftGallery(delta < 0 ? 1 : -1)
    if (galleryDidDragRef.current && document.activeElement instanceof HTMLElement) document.activeElement.blur()
    setGalleryDragging(false)
    setGalleryDragX(0)
  }

  return (
    <main className={`reception-shell ${step === 'select' ? 'rynex-select-shell' : ''}`}>
      {step === 'select' ? <section className="lumis-gallery reception-fade">
        <header className="lumis-gallery-header">
          <img className="lumis-gallery-logo" src="/lumis-logo-dark.png" alt="LUMIS" />
          <div className="lumis-gallery-title"><span>BEM-VINDO</span><h1>Quem você deseja visitar?</h1><small><MoveHorizontal size={15} /> Deslize para explorar</small></div>
        </header>

        {galleryProfessionals.length ? <div ref={galleryRef} className={`lumis-gallery-stage count-${galleryProfessionals.length} ${galleryDragging ? 'is-dragging' : ''}`} style={{ transform: `translateX(${galleryDragX}px)` }} onPointerDown={startGalleryDrag} onPointerMove={moveGalleryDrag} onPointerUp={finishGalleryDrag} onPointerCancel={finishGalleryDrag}>
          {galleryProfessionals.map((professional, index) => {
            const focused = professional.id === galleryFocus?.id
            return <button className={`lumis-gallery-card position-${index} ${focused ? 'is-focused' : ''}`} key={professional.id} type="button" onClick={() => handleGalleryProfessional(professional)} aria-label={`${focused ? 'Visitar' : 'Destacar'} ${professional.name}, sala ${professional.room}`}>
              <span className="lumis-gallery-photo"><img src={receptionPhoto(professional)} alt={`Foto de ${professional.name}`} /></span>
              <span className="lumis-gallery-info"><i><b /> Disponível</i><strong>{professional.name}</strong><small>{professional.profession}</small><span><DoorOpen size={17} /> Sala {professional.room}</span>{focused && <em>Toque para visitar <ArrowRight size={17} /></em>}</span>
            </button>
          })}
        </div> : <div className="lumis-gallery-empty"><Search size={34} /><strong>Nenhum profissional encontrado</strong><span>Tente buscar por outro nome, profissão ou sala.</span></div>}

        <div className="lumis-gallery-actions">
          <label className="lumis-gallery-search"><Search size={25} /><input value={professionalQuery} onChange={(event) => setProfessionalQuery(event.target.value)} placeholder="Buscar profissional ou sala" aria-label="Buscar profissional ou sala" />{professionalQuery && <button type="button" onClick={() => setProfessionalQuery('')} aria-label="Limpar busca">×</button>}</label>
          <button className="lumis-gallery-rent" type="button" onClick={() => setRentalOpen(true)}><BriefcaseBusiness size={23} /><span><small>Espaços profissionais</small><strong>Quero alugar um espaço</strong></span><ArrowRight size={21} /></button>
        </div>
      </section> : <>
        <header className="reception-header">
          <a className="brand" href="/recepcao" aria-label="LUMIS, início"><img className="rynex-header-logo" src="/lumis-logo.png" alt="LUMIS" /></a>
          <button className="reception-back" onClick={reset} type="button"><ArrowLeft size={18} /> Voltar ao início</button>
        </header>
      </>}

      {step === 'identify' && selected && <section className="reception-step reception-fade">
        <div className="step-progress"><span className="active">1</span><i /><span>2</span><i /><span>3</span></div>
        <div className="visit-badge"><img src={selected.photo} alt="" /><span>Visita para<strong>{selected.name} · Sala {selected.room}</strong></span></div>
        <span className="step-icon"><UserRound size={30} /></span><span className="eyebrow">Sua identificação</span><h1>Qual é o seu nome?</h1><p>Precisamos dessa informação para avisar sobre sua chegada.</p>
        <form className="visitor-form" onSubmit={continueToCamera}><label htmlFor="visitor-name">Nome completo</label><input id="visitor-name" autoFocus value={visitorName} onChange={(event) => setVisitorName(event.target.value)} placeholder="Digite seu nome aqui" autoComplete="name" /><button className="touch-primary" disabled={visitorName.trim().length < 2}>Continuar <ArrowRight size={22} /></button></form>
        <div className="privacy-note"><ShieldCheck size={17} /> Seus dados são usados somente para registrar esta visita.</div>
      </section>}

      {step === 'camera' && selected && <section className="reception-step reception-step-wide reception-fade">
        <div className="step-progress"><span className="done"><Check size={16} /></span><i className="done" /><span className="active">2</span><i /><span>3</span></div>
        <span className="eyebrow">Registro de entrada</span><h1>Vamos registrar sua chegada</h1><p>Posicione-se em frente à câmera.</p>
        <div className="camera-layout">
          <div className="camera-preview">
            {photo ? <img src={photo} alt="Foto registrada do visitante" /> : cameraError && !streamRef.current ? <div className="camera-placeholder"><Camera size={44} /><strong>Câmera indisponível</strong><span>Você pode seguir sem a foto nesta apresentação.</span></div> : <video ref={videoRef} autoPlay muted playsInline />}
            {!photo && !cameraError && <div className="face-guide" />}
            <span className="camera-label"><i /> {photo ? 'Foto registrada' : 'Câmera ativa'}</span>
          </div>
          <div className="camera-actions"><div><strong>{photo ? 'Gostou da foto?' : 'Enquadre seu rosto'}</strong><p>{photo ? 'Você pode confirmar ou tirar uma nova foto.' : 'Mantenha o rosto centralizado e toque no botão quando estiver pronto.'}</p></div>{cameraError && <small className="camera-warning">{cameraError}</small>}
            {!photo ? <button className="touch-secondary" onClick={takePhoto} type="button"><Camera size={21} /> Tirar foto</button> : <button className="touch-secondary" onClick={() => { setPhoto(''); setCameraError('') }} type="button"><RefreshCw size={20} /> Tirar novamente</button>}
            <button className="touch-primary" onClick={confirm} type="button">Confirmar chegada <ArrowRight size={22} /></button>
          </div>
        </div>
      </section>}

      {step === 'success' && selected && <section className="reception-success reception-fade">
        <div className="success-rings"><span><Check size={52} strokeWidth={2.4} /></span></div><span className="eyebrow">Chegada registrada</span><h1>Pronto, {visitorName.split(' ')[0]}!</h1><p><strong>{selected.name}</strong> foi avisado que você está aguardando.</p><div className="waiting-card"><img src={selected.photo} alt="" /><span><small>Seu atendimento</small><strong>{selected.name}</strong><b>Sala {selected.room}</b></span></div><div className="auto-return"><span><i /></span> Esta tela voltará ao início em alguns segundos</div><button className="reception-home-link" type="button" onClick={reset}>Voltar agora</button>
      </section>}

      <Modal open={rentalOpen} onClose={closeRental} title={rentalSuccess ? 'Interesse registrado' : 'Alugue uma sala no LUMIS'} subtitle={rentalSuccess ? 'Nossa equipe entrará em contato com você.' : 'Conte um pouco sobre você para receber mais informações.'}>
        {rentalSuccess ? <div className="rental-success"><span><Check size={32} /></span><h3>Obrigado, {rentalForm.name.replace(/^(dr(a)?\.?\s+)/i, '').split(' ')[0]}!</h3><p>Recebemos seus dados. Em breve, nossa equipe apresentará os espaços disponíveis e as condições de locação.</p><button className="primary-button full-button" type="button" onClick={closeRental}>Concluir</button></div> : <form className="rental-form" onSubmit={submitRentalInterest}><div className="rental-form-intro"><span><BriefcaseBusiness size={22} /></span><p>Espaços profissionais em um ambiente moderno, seguro e bem localizado.</p></div><label className="field-label">Nome completo<input className="field-input" required value={rentalForm.name} onChange={(event) => setRentalForm({ ...rentalForm, name: event.target.value })} placeholder="Como podemos chamar você?" /></label><label className="field-label">Profissão / especialidade<input className="field-input" required value={rentalForm.profession} onChange={(event) => setRentalForm({ ...rentalForm, profession: event.target.value })} placeholder="Ex.: Médico cardiologista" /></label><label className="field-label">Telefone / WhatsApp<input className="field-input" required value={rentalForm.phone} onChange={(event) => setRentalForm({ ...rentalForm, phone: event.target.value })} placeholder="(92) 99999-9999" /></label><button className="primary-button full-button rental-submit" type="submit"><Phone size={18} /> Quero receber informações</button></form>}
      </Modal>

      {step !== 'select' && <footer className="reception-footer">LUMIS <span>•</span> Uma recepção mais simples e acolhedora</footer>}
    </main>
  )
}
