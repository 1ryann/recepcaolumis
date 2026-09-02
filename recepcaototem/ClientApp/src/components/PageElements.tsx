import { type ReactNode } from 'react'

export function PageHeader({ eyebrow, title, description, action }: { eyebrow?: string; title: string; description: string; action?: ReactNode }) {
  return (
    <div className="page-header">
      <div>{eyebrow && <span className="page-eyebrow">{eyebrow}</span>}<h1>{title}</h1><p>{description}</p></div>
      {action && <div className="page-action">{action}</div>}
    </div>
  )
}

export function StatusBadge({ status }: { status: 'paid' | 'pending' | 'overdue' | 'active' | 'inactive' | 'occupied' | 'available' }) {
  const labels = { paid: 'Pago', pending: 'Pendente', overdue: 'Atrasado', active: 'Ativo', inactive: 'Inativo', occupied: 'Ocupada', available: 'Disponível' }
  return <span className={`status-badge status-${status}`}><i />{labels[status]}</span>
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <div className="empty-state">{children}</div>
}
