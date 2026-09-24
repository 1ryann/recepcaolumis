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
import customerHomeSource from '../pages/customer/CustomerHome.tsx?raw'
import professionalHomeSource from '../pages/professional/ProfessionalHome.tsx?raw'
import professionalAvailabilitySource from '../pages/professional/ProfessionalAvailability.tsx?raw'
import professionalLeasesSource from '../pages/professional/ProfessionalLeases.tsx?raw'
import professionalProfileSource from '../pages/professional/ProfessionalProfile.tsx?raw'
import professionalReservationsSource from '../pages/professional/ProfessionalReservations.tsx?raw'
import professionalVisitsSource from '../pages/professional/ProfessionalVisits.tsx?raw'
import { trilhas } from './content'

const fontes = [
  adminLayoutSource, customersSource, leasesSource, professionalsSource, roomsSource, settingsSource,
  customerHomeSource, professionalHomeSource, professionalAvailabilitySource, professionalLeasesSource,
  professionalProfileSource, professionalReservationsSource, professionalVisitsSource,
].join('\n')

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

test.each(['admin', 'profissional', 'cliente'] as const)('a trilha %s tem passos ancorados', id => {
  const trilha = trilhas.find(item => item.id === id)!
  expect(trilha.passos.filter(passo => passo.alvo).length).toBeGreaterThanOrEqual(4)
})

// Cada trilha termina apontando o atalho da ajuda, e cada portal tem o seu: a barra lateral
// do painel, a do profissional e a do cliente são componentes diferentes.
test.each([
  ['admin', 'ajuda'],
  ['profissional', 'ajuda-profissional'],
  ['cliente', 'ajuda-cliente'],
] as const)('a trilha %s aponta o atalho da ajuda do próprio portal', (id, alvo) => {
  const trilha = trilhas.find(item => item.id === id)!
  expect(trilha.passos.some(passo => passo.alvo === alvo)).toBe(true)
})
