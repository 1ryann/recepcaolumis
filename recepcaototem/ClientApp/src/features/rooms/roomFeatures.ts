import { AirVent, Accessibility, Armchair, DoorOpen, Droplets, Sun, Wifi, type LucideIcon } from 'lucide-react'
import type { RoomAmenity, RoomCategory } from '../../api/modules'

// The single place that turns a stored code into something a person reads. The admin form,
// the catalogue card and the detail page all go through here, so a room is described in
// the same words wherever it appears, and adding an amenity is one entry rather than three.

export const ROOM_CATEGORIES: readonly RoomCategory[] = ['CONSULTORIO', 'REUNIAO', 'CRIATIVA']

const CATEGORY_LABEL: Record<RoomCategory, string> = {
  CONSULTORIO: 'Consultório',
  REUNIAO: 'Reunião',
  CRIATIVA: 'Criativa',
}

export const ROOM_AMENITIES: readonly RoomAmenity[] =
  ['CLIMATIZADA', 'MOBILIADA', 'WIFI', 'JANELA', 'PIA', 'ACESSIVEL']

const AMENITY_LABEL: Record<RoomAmenity, string> = {
  CLIMATIZADA: 'Climatizada',
  MOBILIADA: 'Mobiliada',
  WIFI: 'Wi-Fi',
  JANELA: 'Janela',
  PIA: 'Pia',
  ACESSIVEL: 'Acessível',
}

const AMENITY_ICON: Record<RoomAmenity, LucideIcon> = {
  CLIMATIZADA: AirVent,
  MOBILIADA: Armchair,
  WIFI: Wifi,
  JANELA: Sun,
  PIA: Droplets,
  ACESSIVEL: Accessibility,
}

// An unknown code can only reach here if the backend gained a value this build does not
// know yet. Showing the raw code beats showing nothing, and beats crashing the page.
export const categoryLabel = (category: RoomCategory | null): string | null =>
  category ? CATEGORY_LABEL[category] ?? category : null

export const amenityLabel = (amenity: RoomAmenity): string => AMENITY_LABEL[amenity] ?? amenity

export const amenityIcon = (amenity: RoomAmenity): LucideIcon => AMENITY_ICON[amenity] ?? DoorOpen

/** "4 pessoas" / "4 a 8 pessoas" / nothing at all. */
export function capacityLabel(minimum: number | null, maximum: number | null): string | null {
  if (minimum === null) return null
  if (maximum === null || maximum === minimum) return `${minimum} pessoas`
  return `${minimum} a ${maximum} pessoas`
}

/** The compact form for a catalogue chip: "4p" / "4p-8p". */
export function capacityChip(minimum: number | null, maximum: number | null): string | null {
  if (minimum === null) return null
  return maximum === null || maximum === minimum ? `${minimum}p` : `${minimum}p-${maximum}p`
}

/** "25m²", trimmed of a pointless ",00". */
export function areaLabel(area: number | null): string | null {
  if (area === null) return null
  return `${area.toLocaleString('pt-BR', { maximumFractionDigits: 2 })}m²`
}

/** "1 Banheiro" / "2 Banheiros" / "Sem banheiro" — a room genuinely may have none. */
export function bathroomLabel(count: number | null): string | null {
  if (count === null) return null
  if (count === 0) return 'Sem banheiro'
  return count === 1 ? '1 Banheiro' : `${count} Banheiros`
}
