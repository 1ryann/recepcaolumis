import { Clock3, LogOut } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { professionalRegistrationApi, type ProfessionalApplicationDto } from '../../api/modules'
import { useSession } from '../../auth/SessionProvider'

export function ProfessionalApplicationStatus() {
  const [application, setApplication] = useState<ProfessionalApplicationDto | null>(null)
  const [error, setError] = useState('')
  const session = useSession(); const navigate = useNavigate()
  useEffect(() => { const controller = new AbortController(); professionalRegistrationApi.me(controller.signal).then(setApplication).catch(() => { if (!controller.signal.aborted) setError('Não foi possível consultar sua solicitação.') }); return () => controller.abort() }, [])
  const logout = async () => { await session.logout(); navigate('/profissional/login', { replace: true }) }
  const rejected = application?.status === 'REJECTED'
  const approved = application?.status === 'APPROVED'
  return <main className="application-status-page"><section className="application-status-card"><span className="login-icon"><Clock3 size={22}/></span><span className="page-eyebrow">Área do profissional</span><h1>{approved ? 'Cadastro aprovado' : rejected ? 'Cadastro não aprovado' : 'Cadastro em análise'}</h1>{application && <><strong>{application.name}</strong><p>{application.profession}</p><small>Solicitado em {new Date(application.createdAt).toLocaleDateString('pt-BR')}</small></>}<p>{approved ? 'Seu acesso profissional foi liberado. Entre novamente para atualizar sua sessão.' : rejected ? 'Sua solicitação foi analisada e não foi aprovada. Procure a gerência caso precise de ajuda.' : 'Sua solicitação foi enviada e está aguardando aprovação da gerência.'}</p>{error && <div className="form-error">{error}</div>}<button className={approved ? 'primary-button' : 'secondary-button'} onClick={logout}><LogOut size={16}/> {approved ? 'Entrar na área profissional' : 'Sair'}</button></section></main>
}
