import { trilhaAdmin } from './admin'
import { trilhaCliente } from './cliente'
import { trilhaProfissional } from './profissional'
import type { Passo, Trilha, TrilhaId } from './types'

export type { Passo, Trilha, TrilhaId }

export const trilhas: Trilha[] = [trilhaAdmin, trilhaProfissional, trilhaCliente]

const trilhaPorRole: Record<string, TrilhaId> = {
  ADMINISTRADOR: 'admin',
  GERENTE: 'admin',
  PROFISSIONAL: 'profissional',
}

export function trilhaPorId(id: TrilhaId) {
  return trilhas.find(trilha => trilha.id === id)
}

export function trilhaDaRole(roles: string[]) {
  for (const role of roles) {
    const id = trilhaPorRole[role]
    if (id) return trilhaPorId(id)
  }
  return undefined
}

export function trilhasPublicas() {
  return trilhas.filter(trilha => trilha.publica)
}

export function passosVisiveis(trilha: Trilha, roles: string[]): Passo[] {
  return trilha.passos.filter(passo => !passo.somenteRoles || passo.somenteRoles.some(role => roles.includes(role)))
}
