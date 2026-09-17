import { ArrowRight, CalendarDays, Stethoscope } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { LumisBackground } from '../features/lumis/LumisBackground'
import { LumisLogo } from '../theme/LumisLogo'

// The official public entrance of Lumis. It is a portal, not a dashboard and not the
// Totem: three deliberate ways in (Cliente, Profissional, equipe). It stays a valid
// public route — an active session only changes where each access points and its label,
// it never redirects `/` on its own. Reads the existing SessionProvider; it must render
// even while the session is loading or if the session check fails.

type Access = { href: string; label: string; description: string; ready: boolean }

export function Home() {
  const { status, user } = useSession()
  const roles = status === 'authenticated' && user ? user.roles : []
  const authed = (role: string) => roles.includes(role)

  const cliente: Access = authed('CUSTOMER')
    ? { href: '/cliente', label: 'Ir para minha área', description: 'Agende e acompanhe seus atendimentos.', ready: true }
    : { href: '/cliente/login', label: 'Área do Cliente', description: 'Agende e acompanhe seus atendimentos.', ready: true }

  const profissional: Access = authed('PROFISSIONAL')
    ? { href: '/profissional', label: 'Ir para minha área', description: 'Agenda, atendimentos e disponibilidade.', ready: true }
    : { href: '/profissional/login', label: 'Área do Profissional', description: 'Agenda, atendimentos e disponibilidade.', ready: true }

  const team: Access = authed('ADMINISTRADOR')
    ? { href: '/admin', label: 'Ir para Administração', description: '', ready: true }
    : authed('GERENTE')
      ? { href: '/recepcao', label: 'Ir para Recepção', description: '', ready: true }
      : { href: '/login', label: 'Acesso da equipe', description: '', ready: true }

  return (
    <main className="home-portal">
      <LumisBackground />
      <div className="home-inner">
        <header className="home-head">
          <LumisLogo className="home-logo" alt="LUMIS" width={132} height={40} />
        </header>

        <section className="home-hero">
          <h1>Gestão inteligente<br />do seu espaço.</h1>
          <p>Agendamentos, atendimentos e organização em um único ambiente.</p>
        </section>

        <nav className="home-access" aria-label="Acessos do Lumis">
          <Link className="home-card" to={cliente.href}>
            <span className="home-card-icon" aria-hidden="true"><CalendarDays size={22} /></span>
            <span className="home-card-body">
              <strong>{cliente.label}</strong>
              <small>{cliente.description}</small>
            </span>
            <ArrowRight className="home-card-arrow" size={20} aria-hidden="true" />
          </Link>

          <Link className="home-card" to={profissional.href}>
            <span className="home-card-icon" aria-hidden="true"><Stethoscope size={22} /></span>
            <span className="home-card-body">
              <strong>{profissional.label}</strong>
              <small>{profissional.description}</small>
            </span>
            <ArrowRight className="home-card-arrow" size={20} aria-hidden="true" />
          </Link>
        </nav>

        <Link className="home-team" to={team.href}>
          {team.label} <ArrowRight size={15} aria-hidden="true" />
        </Link>
      </div>

      <footer className="home-footer">
        Desenvolvido por <strong>RYNEX</strong>
      </footer>
    </main>
  )
}
