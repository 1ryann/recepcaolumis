import { expect, test, vi } from 'vitest'
import { chaveProgresso, criarTourProgressStore } from './tourStorage'

test('a chave segue o formato acordado e não usa o prefixo proibido', () => {
  expect(chaveProgresso('admin')).toBe('lumis.tour.admin.v1')
  expect(chaveProgresso('cliente')).not.toContain('atrium_')
})

test('grava e lê o progresso de uma trilha', () => {
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'concluido', ultimoPasso: 'admin-navegacao', versao: 1 })
  expect(store.ler('admin')).toEqual({ estado: 'concluido', ultimoPasso: 'admin-navegacao', versao: 1 })
})

test('trilha sem progresso lê nulo', () => {
  expect(criarTourProgressStore().ler('profissional')).toBeNull()
})

test('limpar remove o progresso', () => {
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'pulado', ultimoPasso: 'admin-navegacao', versao: 1 })
  store.limpar('admin')
  expect(store.ler('admin')).toBeNull()
})

test('conteúdo corrompido lê nulo em vez de lançar', () => {
  localStorage.setItem(chaveProgresso('admin'), 'não é json')
  expect(criarTourProgressStore().ler('admin')).toBeNull()
})

test('segue funcionando em memória quando o localStorage lança', () => {
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('bloqueado') })
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('bloqueado') })
  const store = criarTourProgressStore()
  store.gravar('admin', { estado: 'pulado', ultimoPasso: 'admin-navegacao', versao: 1 })
  expect(store.ler('admin')).toEqual({ estado: 'pulado', ultimoPasso: 'admin-navegacao', versao: 1 })
})
