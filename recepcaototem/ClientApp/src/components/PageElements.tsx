import { type ReactNode } from 'react'

export function PageHeader({ eyebrow, title, description, action, tour }: { eyebrow?: string; title: string; description: string; action?: ReactNode; tour?: string }) {
  return (
    <div className="page-header" data-tour={tour}>
      <div>{eyebrow && <span className="page-eyebrow">{eyebrow}</span>}<h1>{title}</h1><p>{description}</p></div>
      {action && <div className="page-action">{action}</div>}
    </div>
  )
}

type LegacyStatus = 'paid' | 'pending' | 'overdue' | 'active' | 'inactive' | 'occupied' | 'available'
type Tone = LegacyStatus | 'cancelled' | 'rejected' | 'approved' | 'waiting' | 'in-service' | 'ended'

// StatusBadge originally took a fixed { status } union with a built-in pt-BR label map.
// Reservas/Atendimentos/Locações/Financeiro each need their own label per status (spec
// §7-§10), so the new shape is { tone, label } with the caller supplying the label.
// Two real callers (admin/Professionals.tsx, admin/Rooms.tsx) still use `status`, so
// this stays a union overload instead of a hard breaking change — see task-1 commit notes.
type StatusBadgeProps = { status: LegacyStatus } | { tone: Tone; label: string }

export function StatusBadge(props: StatusBadgeProps) {
  if ('tone' in props) {
    return <span className={`status-badge status-${props.tone}`}><i />{props.label}</span>
  }
  const labels: Record<LegacyStatus, string> = { paid: 'Pago', pending: 'Pendente', overdue: 'Atrasado', active: 'Ativo', inactive: 'Inativo', occupied: 'Ocupada', available: 'Disponível' }
  return <span className={`status-badge status-${props.status}`}><i />{labels[props.status]}</span>
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <div className="empty-state">{children}</div>
}

// "1 visita" / "3 visitas" for the table toolbars' counters.
export function countLabel(count: number, singular: string, plural: string) {
  return `${count} ${count === 1 ? singular : plural}`
}
