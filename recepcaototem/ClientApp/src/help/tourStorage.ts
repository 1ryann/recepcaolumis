import type { TrilhaId } from './content'

export type ProgressoTour = { estado: 'concluido' | 'pulado'; ultimoPasso: string; versao: number }

export type TourProgressStore = {
  ler(id: TrilhaId): ProgressoTour | null
  gravar(id: TrilhaId, progresso: ProgressoTour): void
  limpar(id: TrilhaId): void
}

export function chaveProgresso(id: TrilhaId) {
  return `lumis.tour.${id}.v1`
}

function valido(valor: unknown): valor is ProgressoTour {
  if (typeof valor !== 'object' || valor === null) return false
  const candidato = valor as Partial<ProgressoTour>
  return (candidato.estado === 'concluido' || candidato.estado === 'pulado') && typeof candidato.ultimoPasso === 'string' && typeof candidato.versao === 'number'
}

export function criarTourProgressStore(): TourProgressStore {
  const memoria = new Map<TrilhaId, ProgressoTour>()
  return {
    ler(id) {
      try {
        const bruto = localStorage.getItem(chaveProgresso(id))
        if (bruto === null) return memoria.get(id) ?? null
        const analisado = JSON.parse(bruto) as unknown
        return valido(analisado) ? analisado : null
      } catch { return memoria.get(id) ?? null }
    },
    gravar(id, progresso) {
      memoria.set(id, progresso)
      try { localStorage.setItem(chaveProgresso(id), JSON.stringify(progresso)) } catch { /* armazenamento indisponível */ }
    },
    limpar(id) {
      memoria.delete(id)
      try { localStorage.removeItem(chaveProgresso(id)) } catch { /* armazenamento indisponível */ }
    },
  }
}
