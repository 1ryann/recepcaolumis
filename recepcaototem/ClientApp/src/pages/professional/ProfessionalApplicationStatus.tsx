import { LogOut } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { professionalRegistrationApi, type ProfessionalApplicationDto } from '../../api/modules'
import { useSession } from '../../auth/SessionProvider'
import { LumisPageShell } from '../../features/lumis/LumisPageShell'

export function ProfessionalApplicationStatus() {
  const [application, setApplication] = useState<ProfessionalApplicationDto | null>(null)
  const [error, setError] = useState('')
  const session = useSession(); const navigate = useNavigate()
  useEffect(() => { const controller = new AbortController(); professionalRegistrationApi.me(controller.signal).then(setApplication).catch(() => { if (!controller.signal.aborted) setError('Não foi possível consultar sua solicitação.') }); return () => controller.abort() }, [])
  const logout = async () => { await session.logout(); navigate('/profissional/login', { replace: true }) }
  const rejected = application?.status === 'REJECTED'
  const approved = application?.status === 'APPROVED'
  const statusClass = approved ? 'lumis-status-positive' : rejected ? 'lumis-status-negative' : undefined
  return <LumisPageShell className="lumis-login"><main className="lumis-login-card lumis-auth-surface is-wide"><span className="lumis-login-eyebrow">ÁREA DO PROFISSIONAL</span><h1 className={statusClass}>{approved ? 'Cadastro aprovado' : rejected ? 'Cadastro não aprovado' : 'Cadastro em análise'}</h1>{application && <p className="lumis-login-text"><strong>{application.name}</strong><br />{application.profession}<br /><small>Solicitado em {new Date(application.createdAt).toLocaleDateString('pt-BR')}</small></p>}<p className="lumis-login-text">{approved ? 'Seu acesso profissional foi liberado. Entre novamente para atualizar sua sessão.' : rejected ? 'Sua solicitação foi analisada e não foi aprovada. Procure a gerência caso precise de ajuda.' : 'Sua solicitação foi enviada e está aguardando aprovação da gerência.'}</p>{error && <div className="form-error">{error}</div>}<button className={approved ? 'primary-button lumis-login-submit' : 'secondary-button'} onClick={logout}><LogOut size={16}/> {approved ? 'Entrar na área profissional' : 'Sair'}</button></main></LumisPageShell>
}
