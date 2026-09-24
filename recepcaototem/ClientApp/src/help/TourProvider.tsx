import { type ReactNode, useEffect, useMemo, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'
import { passosVisiveis, type Trilha, type TrilhaId, trilhaDaRole } from './content'
import { TourContext } from './TourContext'
import { TourOverlay } from './TourOverlay'
import { criarTourProgressStore, type TourProgressStore } from './tourStorage'

export { useTour } from './TourContext'

const rotasSemTour = ['/login', '/change-password', '/ajuda']

export function TourProvider({ children, store }: { children: ReactNode; store?: TourProgressStore }) {
  const session = useSession()
  const location = useLocation()
  const navigate = useNavigate()
  const progresso = useMemo(() => store ?? criarTourProgressStore(), [store])
  const [trilha, setTrilha] = useState<Trilha | null>(null)
  const [indice, setIndice] = useState(0)

  const roles = session.user?.roles ?? []
  const candidata = useMemo(() => trilhaDaRole(roles), [roles.join(',')])
  const passos = useMemo(() => (trilha ? passosVisiveis(trilha, roles).filter(passo => passo.alvo) : []), [trilha, roles.join(',')])
  const passo = passos[indice] ?? null

  useEffect(() => {
    if (trilha) return
    if (session.status !== 'authenticated' || !candidata) return
    if (rotasSemTour.some(rota => location.pathname.startsWith(rota))) return
    if (passosVisiveis(candidata, roles).filter(item => item.alvo).length === 0) return
    const salvo = progresso.ler(candidata.id)
    if (salvo && salvo.versao === candidata.versao) return
    setTrilha(candidata)
    setIndice(0)
  }, [session.status, candidata, location.pathname, trilha])

  useEffect(() => {
    if (!passo?.rota || passo.rota === location.pathname) return
    navigate(passo.rota)
  }, [passo?.id])

  const encerrar = (estado: 'concluido' | 'pulado') => {
    if (trilha && passo) progresso.gravar(trilha.id, { estado, ultimoPasso: passo.id, versao: trilha.versao })
    setTrilha(null)
    setIndice(0)
  }

  const value = {
    trilha,
    passo,
    indice,
    total: passos.length,
    avancar: () => { if (indice + 1 >= passos.length) encerrar('concluido'); else setIndice(valor => valor + 1) },
    voltar: () => setIndice(valor => Math.max(0, valor - 1)),
    pular: () => encerrar('pulado'),
    reiniciar: (id: TrilhaId) => { progresso.limpar(id); setTrilha(null); setIndice(0); navigate('/admin') },
  }

  return <TourContext.Provider value={value}>{children}<TourOverlay /></TourContext.Provider>
}
