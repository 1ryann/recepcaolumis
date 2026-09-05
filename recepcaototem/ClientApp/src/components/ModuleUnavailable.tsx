import { Construction } from 'lucide-react'

export function ModuleUnavailable({ title }: { title: string }) {
  return (
    <section className="page-enter module-unavailable" aria-labelledby="module-unavailable-title">
      <span className="module-unavailable-icon"><Construction size={25} /></span>
      <h1 id="module-unavailable-title">{title}</h1>
      <p>Módulo ainda não disponível</p>
    </section>
  )
}
