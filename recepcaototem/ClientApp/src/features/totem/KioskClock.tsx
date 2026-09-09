import { useEffect, useState } from 'react'

// Wall clock for the Totem kiosk. Always renders in America/Porto_Velho time so a
// device in another timezone still shows the reception's local hour. The interval is
// cleared on unmount — the kiosk stays mounted for hours, a leak here is not academic.
const ZONE = 'America/Porto_Velho'
const timeFormat = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: ZONE })
const dateFormat = new Intl.DateTimeFormat('pt-BR', { weekday: 'long', day: '2-digit', month: 'long', timeZone: ZONE })

const capitalize = (value: string) => value.charAt(0).toUpperCase() + value.slice(1)

export function KioskClock() {
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 20_000)
    return () => window.clearInterval(id)
  }, [])

  return (
    <div className="totem-clock">
      <strong>{timeFormat.format(now)}</strong>
      <span>{capitalize(dateFormat.format(now))}</span>
    </div>
  )
}
