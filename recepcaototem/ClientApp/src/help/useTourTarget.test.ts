import { renderHook, waitFor } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import { aguardarAlvo, useTourTarget } from './useTourTarget'

test('encontra um alvo que já está no documento', async () => {
  document.body.innerHTML = '<button data-tour="nova-sala">Nova sala</button>'
  await expect(aguardarAlvo('nova-sala')).resolves.toBeInstanceOf(HTMLElement)
})

test('encontra um alvo que aparece depois', async () => {
  document.body.innerHTML = ''
  const promessa = aguardarAlvo('busca-salas', 1000)
  setTimeout(() => { document.body.innerHTML = '<div data-tour="busca-salas"></div>' }, 20)
  await expect(promessa).resolves.toBeInstanceOf(HTMLElement)
})

test('resolve nulo quando o alvo nunca aparece', async () => {
  document.body.innerHTML = ''
  await expect(aguardarAlvo('inexistente', 30)).resolves.toBeNull()
})

test('devolve a primeira ocorrência quando o alvo se repete', async () => {
  document.body.innerHTML = '<div data-tour="acoes-profissional" id="um"></div><div data-tour="acoes-profissional" id="dois"></div>'
  const encontrado = await aguardarAlvo('acoes-profissional')
  expect(encontrado?.id).toBe('um')
})

test('não vaza observadores após encontrar o alvo', async () => {
  const desconectar = vi.fn()
  vi.spyOn(globalThis, 'MutationObserver').mockImplementation(function () { return { observe: vi.fn(), disconnect: desconectar, takeRecords: vi.fn() } } as unknown as typeof MutationObserver)
  document.body.innerHTML = ''
  await aguardarAlvo('inexistente', 20)
  expect(desconectar).toHaveBeenCalled()
})

test('trata um alvo fora da tela como ausente', async () => {
  document.body.innerHTML = '<div data-tour="nav-lateral"></div>'
  const elemento = document.querySelector('[data-tour="nav-lateral"]') as HTMLElement
  elemento.getBoundingClientRect = () => ({
    top: 40, left: -300, width: 245, height: 400, right: -55, bottom: 440, x: -300, y: 40, toJSON() { return {} },
  })
  const { result } = renderHook(() => useTourTarget('nav-lateral'))
  await waitFor(() => expect(result.current.elemento).toBeNull())
  expect(result.current.retangulo).toBeNull()
})
