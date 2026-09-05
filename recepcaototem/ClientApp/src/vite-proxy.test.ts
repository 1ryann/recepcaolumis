import { expect, test } from 'vitest'
import viteConfig from '../vite.config'
import apiClientSource from './api/client.ts?raw'

test('development server proxies API requests to the local ASP.NET Core HTTPS host', () => {
  const proxy = viteConfig.server?.proxy as Record<string, { target?: string; secure?: boolean }> | undefined

  expect(proxy?.['/api']).toMatchObject({ target: 'https://localhost:7266', secure: false })
})

test('React client preserves relative API URLs', () => {
  expect(apiClientSource).not.toContain('localhost:5218')
})
