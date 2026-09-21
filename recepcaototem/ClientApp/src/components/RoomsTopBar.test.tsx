import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { expect, test } from 'vitest'
import { ThemeProvider } from '../theme/ThemeProvider'
import { RoomsTopBar } from './RoomsTopBar'

const renderBar = (surface: 'kiosk' | 'public') =>
  render(
    <ThemeProvider><MemoryRouter><RoomsTopBar surface={surface} /></MemoryRouter></ThemeProvider>,
  )

test('a visitor in a browser gets a link to the catalogue', () => {
  renderBar('public')

  expect(screen.getByRole('link', { name: 'Salas' })).toHaveAttribute('href', '/salas')
})

// No account control in this bar at all. The page is a public listing; a sign-in button in
// the corner is the one thing on the screen that leads away from the room the visitor came
// to look at. The bar does not read the session, which is why there is no signed-in case
// here and no SessionProvider in this file.
test('no account control appears in the bar', () => {
  renderBar('public')

  expect(screen.queryByRole('link', { name: /entrar|minha conta|perfil|login/i })).not.toBeInTheDocument()
})

// The kiosk stands in the lobby: every destination is somewhere the person standing at it
// cannot come back from.
test('the kiosk bar offers no navigation at all', () => {
  renderBar('kiosk')

  expect(screen.queryByRole('link', { name: 'Salas' })).not.toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /entrar|minha conta/i })).not.toBeInTheDocument()
})

test('the logo goes back to the catalogue of whichever surface the visitor is on', () => {
  renderBar('kiosk')

  expect(screen.getByRole('link', { name: /voltar para salas/i })).toHaveAttribute('href', '/totem/salas')
})
