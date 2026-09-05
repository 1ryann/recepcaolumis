import type { ReactNode } from 'react'
import { AppStoreProvider } from './AppStore'

export function DevelopmentAppStore({ children }: { children: ReactNode }) {
  return <AppStoreProvider>{children}</AppStoreProvider>
}
