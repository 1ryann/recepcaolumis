import { expect, test } from 'vitest'
import { passosVisiveis, trilhaDaRole, trilhaPorId, trilhas, trilhasPublicas } from './content'

test('cada trilha tem ids de passo únicos', () => {
  for (const trilha of trilhas) {
    const ids = trilha.passos.map(passo => passo.id)
    expect(new Set(ids).size, `ids duplicados na trilha ${trilha.id}`).toBe(ids.length)
  }
})

test('cada trilha tem título, resumo, versão e ao menos um passo', () => {
  for (const trilha of trilhas) {
    expect(trilha.titulo.length).toBeGreaterThan(0)
    expect(trilha.resumo.length).toBeGreaterThan(0)
    expect(trilha.versao).toBeGreaterThanOrEqual(1)
    expect(trilha.passos.length).toBeGreaterThan(0)
  }
})

test('o administrador recebe a trilha do painel', () => {
  expect(trilhaDaRole(['ADMINISTRADOR'])?.id).toBe('admin')
})

test('o gerente não recebe a trilha do administrador, porque o portal dele é /recepcao e /admin nega o acesso', () => {
  expect(trilhaDaRole(['GERENTE'])).toBeUndefined()
})

test('profissional e cliente recebem as próprias trilhas', () => {
  expect(trilhaDaRole(['PROFISSIONAL'])?.id).toBe('profissional')
  expect(trilhaDaRole(['CUSTOMER'])?.id).toBe('cliente')
})

test('role desconhecida não recebe trilha', () => {
  expect(trilhaDaRole(['OUTRA'])).toBeUndefined()
  expect(trilhaDaRole([])).toBeUndefined()
})

test('as trilhas de profissional e cliente são públicas e a de admin não', () => {
  const publicas = trilhasPublicas().map(trilha => trilha.id)
  expect(publicas).toContain('profissional')
  expect(publicas).toContain('cliente')
  expect(publicas).not.toContain('admin')
})

test('passosVisiveis remove passos restritos a outra role', () => {
  const trilha = trilhaPorId('admin')!
  const restrito = { id: 'restrito-teste', titulo: 'T', texto: 'X', somenteRoles: ['ADMINISTRADOR'] }
  const alterada = { ...trilha, passos: [...trilha.passos, restrito] }
  expect(passosVisiveis(alterada, ['GERENTE']).map(passo => passo.id)).not.toContain('restrito-teste')
  expect(passosVisiveis(alterada, ['ADMINISTRADOR']).map(passo => passo.id)).toContain('restrito-teste')
})
