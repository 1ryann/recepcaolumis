import { ChevronDown, KeyRound, LogOut } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useSession } from '../auth/SessionProvider'

const roleLabels: Record<string, string> = { ADMINISTRADOR: 'Administrador', GERENTE: 'Gerente' }

// The profile chip at the right of the admin and reception top bars. It used to carry a
// chevron that opened nothing; it now opens the account actions (change password, sign out).
// The reception area had no top bar at all, so a manager had no way to sign out.
export function AccountMenu() {
  const session = useSession()
  const navigate = useNavigate()
  const location = useLocation()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const name = session.user?.displayName || session.user?.email || 'Usuário'
  const initials = name.split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase()
  const rawRole = session.user?.roles[0] ?? ''
  const role = roleLabels[rawRole] ?? rawRole
  const logout = async () => { await session.logout(); navigate('/login') }

  useEffect(() => {
    if (!open) return
    const close = (event: MouseEvent | KeyboardEvent) => {
      if (event instanceof KeyboardEvent ? event.key === 'Escape' : !ref.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', close)
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', close) }
  }, [open])
  useEffect(() => setOpen(false), [location.pathname])

  return (
    <div className="profile-menu" ref={ref}>
      <button className="profile-chip" type="button" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(value => !value)}>
        <span>{initials}</span><div><strong>{name}</strong><small>{role}</small></div><ChevronDown size={15} />
      </button>
      {open && <div className="profile-menu-list" role="menu">
        <Link role="menuitem" to="/change-password"><KeyRound size={16} /> Alterar senha</Link>
        <button role="menuitem" type="button" onClick={() => void logout()}><LogOut size={16} /> Sair</button>
      </div>}
    </div>
  )
}
