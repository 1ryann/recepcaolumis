import { createContext, type ReactNode, useContext, useEffect, useMemo, useState } from 'react'
import { defaultLeases, defaultProfessionals, defaultRooms, defaultSettings, defaultVisits, type Lease, type Professional, type Room, type SettingsData, type Visit } from './mock'

type NewProfessional = Omit<Professional, 'id'>
type NewLease = Omit<Lease, 'id'>

type AppStore = {
  professionals: Professional[]
  rooms: Room[]
  leases: Lease[]
  visits: Visit[]
  settings: SettingsData
  saveProfessional: (professional: Professional | NewProfessional) => void
  toggleProfessional: (id: string) => void
  addVisit: (visit: Omit<Visit, 'id'>) => void
  addLease: (lease: NewLease) => void
  saveSettings: (settings: SettingsData) => void
}

const StoreContext = createContext<AppStore | null>(null)

function readLocal<T>(key: string, fallback: T): T {
  try {
    const value = localStorage.getItem(key)
    return value ? JSON.parse(value) as T : fallback
  } catch {
    return fallback
  }
}

function readSettings() {
  const saved = readLocal('atrium_settings', defaultSettings)
  return /Atrium|RYNEX/i.test(saved.buildingName) ? { ...saved, buildingName: 'LUMIS' } : saved
}

export function AppStoreProvider({ children }: { children: ReactNode }) {
  const [professionals, setProfessionals] = useState(() => readLocal('atrium_professionals', defaultProfessionals))
  const [rooms, setRooms] = useState(() => readLocal('atrium_rooms', defaultRooms))
  const [leases, setLeases] = useState(() => readLocal('atrium_leases', defaultLeases))
  const [visits, setVisits] = useState(() => readLocal('atrium_visits', defaultVisits))
  const [settings, setSettings] = useState(readSettings)

  useEffect(() => localStorage.setItem('atrium_professionals', JSON.stringify(professionals)), [professionals])
  useEffect(() => localStorage.setItem('atrium_rooms', JSON.stringify(rooms)), [rooms])
  useEffect(() => localStorage.setItem('atrium_leases', JSON.stringify(leases)), [leases])
  useEffect(() => localStorage.setItem('atrium_visits', JSON.stringify(visits)), [visits])
  useEffect(() => localStorage.setItem('atrium_settings', JSON.stringify(settings)), [settings])

  const value = useMemo<AppStore>(() => ({
    professionals, rooms, leases, visits, settings,
    saveProfessional: (professional) => {
      if ('id' in professional) {
        setProfessionals((current) => current.map((item) => item.id === professional.id ? professional : item))
      } else {
        setProfessionals((current) => [...current, { ...professional, id: `pro-${Date.now()}` }])
      }
    },
    toggleProfessional: (id) => setProfessionals((current) => current.map((item) => item.id === id ? { ...item, active: !item.active } : item)),
    addVisit: (visit) => setVisits((current) => [{ ...visit, id: `v-${Date.now()}` }, ...current]),
    addLease: (lease) => {
      setLeases((current) => [...current, { ...lease, id: `loc-${Date.now()}` }])
      setRooms((current) => current.map((room) => room.number === lease.room ? { ...room, status: 'occupied', professionalId: lease.professionalId } : room))
      setProfessionals((current) => current.map((professional) => professional.id === lease.professionalId ? { ...professional, room: lease.room } : professional))
    },
    saveSettings: setSettings,
  }), [professionals, rooms, leases, visits, settings])

  return <StoreContext.Provider value={value}>{children}</StoreContext.Provider>
}

export function useAppStore() {
  const context = useContext(StoreContext)
  if (!context) throw new Error('useAppStore must be used inside AppStoreProvider')
  return context
}
