import { expect, test } from 'vitest'
import adminLayoutSource from '../components/AdminLayout.tsx?raw'
import appSource from '../App.tsx?raw'
import developmentAppSource from '../dev/DevelopmentApp.tsx?raw'
import professionalsSource from '../pages/admin/Professionals.tsx?raw'
import roomsSource from '../pages/admin/Rooms.tsx?raw'
import { trilhas } from './content'

const fontes = [adminLayoutSource, professionalsSource, roomsSource].join('\n')

test('todo alvo declarado existe como data-tour no código das telas', () => {
  for (const trilha of trilhas) {
    for (const passo of trilha.passos) {
      if (!passo.alvo) continue
      expect(fontes, `alvo ${passo.alvo} do passo ${passo.id} não existe nas telas`).toContain(`data-tour="${passo.alvo}"`)
    }
  }
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

test('a trilha do administrador tem passos ancorados', () => {
  const admin = trilhas.find(trilha => trilha.id === 'admin')!
  expect(admin.passos.filter(passo => passo.alvo).length).toBeGreaterThanOrEqual(7)
})
