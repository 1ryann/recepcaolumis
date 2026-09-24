export type TrilhaId = 'admin' | 'profissional' | 'cliente'

export type Passo = {
  id: string
  titulo: string
  texto: string
  alvo?: string
  rota?: string
  somenteRoles?: string[]
}

export type Trilha = {
  id: TrilhaId
  titulo: string
  resumo: string
  publica: boolean
  versao: number
  passos: Passo[]
}
