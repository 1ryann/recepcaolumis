// `styles.css` is imported FIRST, before anything that reaches a page component, because
// import order decides CSS order in the bundle. Pages that ship their own stylesheet
// (styles.totem-room.css, styles.totem-professionals.css) refine selectors that also live
// in styles.css; with App imported first, those page sheets were emitted *before* it and
// lost every tie on equal specificity. Keep this line above the App import.
import './styles.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { SessionProvider } from './auth/SessionProvider'
import { TourProvider } from './help/TourProvider'
import { ThemeProvider } from './theme/ThemeProvider'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <BrowserRouter>
        <SessionProvider><TourProvider><App /></TourProvider></SessionProvider>
      </BrowserRouter>
    </ThemeProvider>
  </StrictMode>,
)
