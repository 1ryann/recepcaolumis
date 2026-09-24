import { expect, test } from 'vitest'
import adminLayoutSource from '../components/AdminLayout.tsx?raw'
import pageElementsSource from '../components/PageElements.tsx?raw'
import appSource from '../App.tsx?raw'
import developmentAppSource from '../dev/DevelopmentApp.tsx?raw'
import customersSource from '../pages/admin/Customers.tsx?raw'
import leasesSource from '../pages/admin/Leases.tsx?raw'
import professionalsSource from '../pages/admin/Professionals.tsx?raw'
import roomsSource from '../pages/admin/Rooms.tsx?raw'
import settingsSource from '../pages/admin/Settings.tsx?raw'
import { trilhas } from './content'

const fontes = [adminLayoutSource, customersSource, leasesSource, professionalsSource, roomsSource, settingsSource].join('\n')

test('todo alvo declarado existe no código das telas, como data-tour direto ou como prop tour do PageHeader', () => {
  for (const trilha of trilhas) {
    for (const passo of trilha.passos) {
      if (!passo.alvo) continue
      const direto = fontes.includes(`data-tour="${passo.alvo}"`)
      const viaPageHeader = fontes.includes(`tour="${passo.alvo}"`)
      expect(direto || viaPageHeader, `alvo ${passo.alvo} do passo ${passo.id} não existe nas telas`).toBe(true)
    }
  }
})

test('o PageHeader repassa a prop tour como data-tour, senão as âncoras de página não existiriam no DOM', () => {
  expect(pageElementsSource).toContain('data-tour={tour}')
})

test('toda rota declarada está registrada em produção e em desenvolvimento', () => {
  for (const trilha of trilhas) {
    for (const passo of trilha.passos) {
      if (!passo.rota) continue
      const segmento = passo.rota.split('/').filter(Boolean).pop()!
      for (const fonte of [appSource, developmentAppSource]) {
        expect(fonte, `rota ${passo.rota} do passo ${passo.id} não está registrada`).toMatch(new RegExp(`path="/?${segmento}"`))
      }
    }
  }
})

test('nenhum passo ancorado depende de dados já cadastrados', () => {
  const ancorados = trilhas.flatMap(trilha => trilha.passos.filter(passo => passo.alvo).map(passo => passo.alvo))
  for (const alvo of ancorados) {
    expect(alvo, 'âncoras em linha de tabela ou paginação somem numa instalação nova').not.toMatch(/^(acoes|paginacao)-/)
  }
})

test('a trilha do administrador tem passos ancorados', () => {
  const admin = trilhas.find(trilha => trilha.id === 'admin')!
  expect(admin.passos.filter(passo => passo.alvo).length).toBeGreaterThanOrEqual(6)
})
