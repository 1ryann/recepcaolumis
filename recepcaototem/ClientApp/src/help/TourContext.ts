import { createContext, useContext } from 'react'
import type { Passo, Trilha, TrilhaId } from './content'

export type TourContextValue = {
  trilha: Trilha | null
  passo: Passo | null
  indice: number
  total: number
  avancar(): void
  voltar(): void
  pular(): void
  reiniciar(id: TrilhaId): void
}

export const TourContext = createContext<TourContextValue | null>(null)

export function useTour() {
  const value = useContext(TourContext)
  if (!value) throw new Error('useTour must be used inside TourProvider')
  return value
}
