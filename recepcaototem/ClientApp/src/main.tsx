import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { AppStoreProvider } from './store/AppStore'
import { SessionProvider } from './auth/SessionProvider'
import './styles.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AppStoreProvider>
        <SessionProvider><App /></SessionProvider>
      </AppStoreProvider>
    </BrowserRouter>
  </StrictMode>,
)
