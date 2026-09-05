import { expect, test } from 'vitest'
import viteConfig from '../vite.config'
import apiClientSource from './api/client.ts?raw'

test('development server proxies API requests to the local ASP.NET Core host', () => {
  const proxy = viteConfig.server?.proxy as Record<string, { target?: string }> | undefined

  expect(proxy?.['/api']).toMatchObject({ target: 'http://localhost:5218' })
})

test('React client preserves relative API URLs', () => {
  expect(apiClientSource).not.toContain('localhost:5218')
})
