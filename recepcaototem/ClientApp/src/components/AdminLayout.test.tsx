import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { AdminLayout } from './AdminLayout'
import { useSession } from '../auth/SessionProvider'
import { ThemeProvider } from '../theme/ThemeProvider'

vi.mock('../auth/SessionProvider', () => ({ useSession: vi.fn() }))

vi.mocked(useSession).mockReturnValue({
  status: 'authenticated',
  user: { userId: 'u-1', displayName: 'Ana Souza', email: 'ana@lumis.test', roles: ['ADMINISTRADOR'], mustChangePassword: false },
  refresh: vi.fn(),
  login: vi.fn(),
  logout: vi.fn(),
  changePassword: vi.fn(),
} as unknown as ReturnType<typeof useSession>)

function renderAt(path: string) {
  return render(
    <ThemeProvider>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/admin" element={<AdminLayout />}>
            <Route path="interesses-locacao" element={<p>Página de interesses</p>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </ThemeProvider>,
  )
}

test('renders the room rental inquiries nav link inside the Gestão section, before Configurações', () => {
  renderAt('/admin/interesses-locacao')
  const link = screen.getByRole('link', { name: /interesses de locação/i })
  expect(link).toHaveAttribute('href', '/admin/interesses-locacao')
  const settingsLink = screen.getByRole('link', { name: /configurações/i })
  const nav = screen.getByRole('navigation', { name: /navegação administrativa/i })
  const links = Array.from(nav.querySelectorAll('a')).map(item => item.textContent)
  expect(links.indexOf(link.textContent!)).toBeLessThan(links.indexOf(settingsLink.textContent!))
})

test('shows the room rental inquiries page title in the topbar', () => {
  renderAt('/admin/interesses-locacao')
  expect(screen.getByText('Interesses de locação', { selector: '.topbar-title span' })).toBeInTheDocument()
})
