import { ArrowRight, BriefcaseBusiness, CalendarDays, MessageCircle, QrCode, Sparkles, Stethoscope, UserRound } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { RevealOnScroll } from '../features/landing/RevealOnScroll'
import { LumisLogo } from '../theme/LumisLogo'
import '../styles.landing.css'

// The public face of Lumis at `/`. It presents the building to someone who arrived from
// the outside — a professional looking for a room, a client about to be seen here — and
// leads with the single action that matters commercially: renting a room. The three ways
// in (Cliente, Profissional, equipe) stay on the same page, further down, with the same
// session-aware behaviour they always had: an active session only changes where each
// access points and its label, it never redirects `/` on its own. It must render even
// while the session is loading or if the session check fails.

type Access = { href: string; label: string; description: string }

const highlights = [
  { icon: BriefcaseBusiness, title: 'Salas equipadas', text: 'Ambientes prontos para atender, com reserva por hora ou contrato fixo.' },
  { icon: UserRound, title: 'Recepção que acolhe', text: 'Seu cliente é recebido, identificado e encaminhado sem que você precise interromper o atendimento.' },
  { icon: QrCode, title: 'Check-in por QR code', text: 'O cliente chega, faz o check-in no totem e entra na fila do seu atendimento.' },
  { icon: MessageCircle, title: 'Avisos no WhatsApp', text: 'Confirmação, reagendamento, cancelamento e atraso, enviados automaticamente a quem autorizou.' },
]

const steps = [
  { number: '1', title: 'Escolha a sala', text: 'Veja as salas disponíveis, com capacidade, valores e fotos.' },
  { number: '2', title: 'Deixe seu contato', text: 'Registre seu interesse na sala em menos de um minuto.' },
  { number: '3', title: 'A recepção fala com você', text: 'Nossa equipe retorna para combinar a visita e os detalhes do contrato.' },
]

export function Home() {
  const { status, user } = useSession()
  const roles = status === 'authenticated' && user ? user.roles : []
  const authed = (role: string) => roles.includes(role)

  const cliente: Access = authed('CUSTOMER')
    ? { href: '/cliente', label: 'Ir para minha área', description: 'Agende e acompanhe seus atendimentos.' }
    : { href: '/cliente/login', label: 'Área do Cliente', description: 'Agende e acompanhe seus atendimentos.' }

  const profissional: Access = authed('PROFISSIONAL')
    ? { href: '/profissional', label: 'Ir para minha área', description: 'Agenda, atendimentos e disponibilidade.' }
    : { href: '/profissional/login', label: 'Área do Profissional', description: 'Agenda, atendimentos e disponibilidade.' }

  const team: Access = authed('ADMINISTRADOR')
    ? { href: '/admin', label: 'Ir para Administração', description: '' }
    : authed('GERENTE')
      ? { href: '/recepcao', label: 'Ir para Recepção', description: '' }
      : { href: '/login', label: 'Acesso da equipe', description: '' }

  return (
    <main className="landing">
      <LumisBackground />

      {/* Opening: the light parts from the centre and the logo emerges from it. */}
      <div className="landing-intro" aria-hidden="true">
        <span className="landing-intro-glow" />
        <span className="landing-intro-beam" />
        <span className="landing-intro-panel is-left" />
        <span className="landing-intro-panel is-right" />
        <span className="landing-intro-mark">
          <LumisLogo alt="" width={196} height={60} />
        </span>
      </div>

      <div className="landing-inner">
        <header className="landing-head">
          <LumisLogo className="landing-logo" alt="LUMIS" width={132} height={40} />
          <a className="landing-head-link" href="#acessos">Acessar <ArrowRight size={15} aria-hidden="true" /></a>
        </header>

        <section className="landing-hero">
          <span className="landing-eyebrow"><Sparkles size={14} aria-hidden="true" /> Centro empresarial</span>
          <h1>Um espaço pronto<br />para o seu atendimento.</h1>
          <p>
            Salas por hora ou por contrato, recepção que recebe o seu cliente e um sistema que cuida
            da agenda, do check-in e dos avisos. Você só precisa atender.
          </p>
          <div className="landing-cta">
            <Link className="landing-cta-primary" to="/totem/salas">
              Alugar sala <ArrowRight size={18} aria-hidden="true" />
            </Link>
            <Link className="landing-cta-secondary" to="/cliente/login">Sou cliente</Link>
          </div>
        </section>

        <section className="landing-section" aria-labelledby="espaco">
          <RevealOnScroll><h2 id="espaco">O espaço</h2></RevealOnScroll>
          <div className="landing-grid">
            {highlights.map(({ icon: Icon, title, text }, index) => (
              <RevealOnScroll as="article" className="landing-card" key={title} delay={index * 80}>
                <span className="landing-card-icon" aria-hidden="true"><Icon size={20} /></span>
                <strong>{title}</strong>
                <p>{text}</p>
              </RevealOnScroll>
            ))}
          </div>
        </section>

        <section className="landing-section" aria-labelledby="como-funciona">
          <RevealOnScroll><h2 id="como-funciona">Como funciona</h2></RevealOnScroll>
          <ol className="landing-steps">
            {steps.map(({ number, title, text }, index) => (
              <RevealOnScroll as="li" className="landing-step" key={number} delay={index * 80}>
                <span className="landing-step-number" aria-hidden="true">{number}</span>
                <div><strong>{title}</strong><p>{text}</p></div>
              </RevealOnScroll>
            ))}
          </ol>
          <RevealOnScroll delay={80}>
            <Link className="landing-cta-primary landing-steps-cta" to="/totem/salas">
              Ver salas disponíveis <ArrowRight size={18} aria-hidden="true" />
            </Link>
          </RevealOnScroll>
        </section>

        <section className="landing-section" id="acessos" aria-labelledby="acessos-titulo">
          <RevealOnScroll><h2 id="acessos-titulo">Já usa o LUMIS?</h2></RevealOnScroll>
          <RevealOnScroll as="div" delay={60}>
          <nav className="landing-access" aria-label="Acessos do Lumis">
            <Link className="landing-access-card" to={cliente.href}>
              <span className="landing-card-icon" aria-hidden="true"><CalendarDays size={20} /></span>
              <span className="landing-access-body">
                <strong>{cliente.label}</strong>
                <small>{cliente.description}</small>
              </span>
              <ArrowRight className="landing-access-arrow" size={18} aria-hidden="true" />
            </Link>

            <Link className="landing-access-card" to={profissional.href}>
              <span className="landing-card-icon" aria-hidden="true"><Stethoscope size={20} /></span>
              <span className="landing-access-body">
                <strong>{profissional.label}</strong>
                <small>{profissional.description}</small>
              </span>
              <ArrowRight className="landing-access-arrow" size={18} aria-hidden="true" />
            </Link>
          </nav>

          <Link className="landing-team" to={team.href}>
            {team.label} <ArrowRight size={15} aria-hidden="true" />
          </Link>
          </RevealOnScroll>
        </section>
      </div>

      <footer className="landing-footer">
        Desenvolvido por <strong>RYVEX</strong>
      </footer>
    </main>
  )
}
