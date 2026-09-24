import { CircleHelp } from 'lucide-react'
import { Link } from 'react-router-dom'
import { homeForRoles } from '../auth/roleRoutes'
import { useSession } from '../auth/SessionProvider'
import { passosVisiveis, type Trilha, trilhaDaRole, trilhasPublicas } from './content'
import { useTour } from './TourContext'

export function HelpCenter() {
  const session = useSession()
  const { reiniciar } = useTour()
  const roles = session.user?.roles ?? []
  const doPerfil = trilhaDaRole(roles)
  const lista: Trilha[] = doPerfil ? [doPerfil, ...trilhasPublicas().filter(trilha => trilha.id !== doPerfil.id)] : trilhasPublicas()

  return (
    <div className="help-page page-enter">
      <header className="help-header">
        <span className="help-icon"><CircleHelp size={22} /></span>
        <div><h1>Central de ajuda</h1><p>Como usar o LUMIS, separado por quem está usando.</p></div>
        {session.status === 'authenticated' && <Link className="secondary-button" to={homeForRoles(roles)}>Voltar ao painel</Link>}
      </header>
      {lista.map(trilha => {
        const passos = passosVisiveis(trilha, roles)
        // Só a trilha do próprio perfil pode ser refeita: as outras percorrem rotas de outro
        // portal, que o ProtectedRoute nega para quem está lendo aqui.
        const refazivel = trilha.id === doPerfil?.id && passos.some(passo => passo.alvo)
        return (
          <section className="panel help-track" key={trilha.id}>
            <div className="help-track-header">
              <div><h2>{trilha.titulo}</h2><p>{trilha.resumo}</p></div>
              {refazivel && session.status === 'authenticated' && <button className="secondary-button" type="button" onClick={() => reiniciar(trilha.id)}>Refazer o tour</button>}
            </div>
            <ol className="help-steps">
              {passos.map(passo => <li key={passo.id}><strong>{passo.titulo}</strong><p>{passo.texto}</p></li>)}
            </ol>
          </section>
        )
      })}
    </div>
  )
}
