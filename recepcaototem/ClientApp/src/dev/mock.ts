export type Professional = {
  id: string
  name: string
  profession: string
  room: string
  photo: string
  phone: string
  active: boolean
}

export type Room = {
  number: string
  floor: string
  status: 'occupied' | 'available'
  professionalId?: string
}

export type Lease = {
  id: string
  professionalId: string
  room: string
  amount: number
  dueDate: string
  startDate: string
  status: 'paid' | 'pending' | 'overdue'
}

export type Visit = {
  id: string
  date: string
  time: string
  visitor: string
  professionalId: string
  room: string
}

export type SettingsData = {
  buildingName: string
  receptionWhatsapp: string
  camera: string
}

export const defaultProfessionals: Professional[] = [
  { id: 'ana', name: 'Dra. Ana Martins', profession: 'Psicóloga clínica', room: '03', photo: 'https://i.pravatar.cc/600?img=47', phone: '(92) 99124-7821', active: true },
  { id: 'carlos', name: 'Carlos Mendes', profession: 'Advogado empresarial', room: '05', photo: 'https://i.pravatar.cc/600?img=12', phone: '(92) 99288-1405', active: true },
  { id: 'beatriz', name: 'Dra. Beatriz Lima', profession: 'Nutricionista', room: '01', photo: 'https://i.pravatar.cc/600?img=32', phone: '(92) 98401-6372', active: true },
  { id: 'rafael', name: 'Rafael Nogueira', profession: 'Consultor financeiro', room: '07', photo: 'https://i.pravatar.cc/600?img=11', phone: '(92) 99310-2506', active: true },
  { id: 'marina', name: 'Marina Costa', profession: 'Arquiteta', room: '08', photo: 'https://i.pravatar.cc/600?img=44', phone: '(92) 98872-9044', active: true },
]

export const defaultRooms: Room[] = [
  { number: '01', floor: '1º andar', status: 'occupied', professionalId: 'beatriz' },
  { number: '02', floor: '1º andar', status: 'available' },
  { number: '03', floor: '1º andar', status: 'occupied', professionalId: 'ana' },
  { number: '04', floor: '1º andar', status: 'available' },
  { number: '05', floor: '2º andar', status: 'occupied', professionalId: 'carlos' },
  { number: '06', floor: '2º andar', status: 'available' },
  { number: '07', floor: '2º andar', status: 'occupied', professionalId: 'rafael' },
  { number: '08', floor: '2º andar', status: 'occupied', professionalId: 'marina' },
]

export const defaultLeases: Lease[] = [
  { id: 'loc-01', professionalId: 'beatriz', room: '01', amount: 2450, dueDate: '2026-09-05', startDate: '2025-02-10', status: 'paid' },
  { id: 'loc-02', professionalId: 'ana', room: '03', amount: 2800, dueDate: '2026-09-08', startDate: '2024-08-01', status: 'pending' },
  { id: 'loc-03', professionalId: 'carlos', room: '05', amount: 3200, dueDate: '2026-08-28', startDate: '2025-01-15', status: 'overdue' },
  { id: 'loc-04', professionalId: 'rafael', room: '07', amount: 2600, dueDate: '2026-09-10', startDate: '2025-06-01', status: 'pending' },
  { id: 'loc-05', professionalId: 'marina', room: '08', amount: 2950, dueDate: '2026-09-12', startDate: '2024-11-20', status: 'paid' },
]

export const defaultVisits: Visit[] = [
  { id: 'v01', date: '2026-09-02', time: '09:35', visitor: 'Marcos Oliveira', professionalId: 'ana', room: '03' },
  { id: 'v02', date: '2026-09-02', time: '09:12', visitor: 'Juliana Souza', professionalId: 'carlos', room: '05' },
  { id: 'v03', date: '2026-09-02', time: '08:48', visitor: 'Fernando Gomes', professionalId: 'beatriz', room: '01' },
  { id: 'v04', date: '2026-09-02', time: '08:25', visitor: 'Larissa Monteiro', professionalId: 'marina', room: '08' },
  { id: 'v05', date: '2026-09-01', time: '16:40', visitor: 'Paulo Vieira', professionalId: 'rafael', room: '07' },
  { id: 'v06', date: '2026-09-01', time: '15:18', visitor: 'Camila Freitas', professionalId: 'ana', room: '03' },
  { id: 'v07', date: '2026-09-01', time: '13:55', visitor: 'Roberto Alves', professionalId: 'carlos', room: '05' },
  { id: 'v08', date: '2026-08-31', time: '11:30', visitor: 'Patrícia Duarte', professionalId: 'beatriz', room: '01' },
  { id: 'v09', date: '2026-08-31', time: '10:04', visitor: 'André Cardoso', professionalId: 'rafael', room: '07' },
  { id: 'v10', date: '2026-08-30', time: '14:22', visitor: 'Renata Melo', professionalId: 'marina', room: '08' },
]

export const defaultSettings: SettingsData = {
  buildingName: 'LUMIS',
  receptionWhatsapp: '(92) 99110-2020',
  camera: 'Câmera padrão do dispositivo',
}
