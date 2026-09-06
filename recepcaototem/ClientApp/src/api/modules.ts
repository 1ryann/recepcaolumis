import { apiClient } from './client'

export type ModuleStatus = 'all' | 'active' | 'inactive'

export interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface ModuleListQuery {
  search?: string
  status: ModuleStatus
  page: number
  pageSize: number
}

export interface ProfessionalDto {
  id: string
  name: string
  profession: string
  whatsApp: string
  isActive: boolean
  hasPhoto: boolean
  photoUrl: string | null
  hasLinkedUser: boolean
  createdAt: string
  updatedAt: string
  concurrencyToken: string
}

export interface ProfessionalInput {
  name: string
  profession: string
  whatsApp: string
}

export interface EligibleUserDto {
  userId: string
  displayName: string
  email: string
}

export type ProfessionalUserLinkDto =
  | { linked: false }
  | { linked: true, userId: string, displayName: string, email: string }

export interface RoomDto {
  id: string
  name: string
  description: string | null
  hourlyRate: number
  dailyRate: number
  isActive: boolean
  createdAt: string
  updatedAt: string
  concurrencyToken: string
}

export interface RoomInput {
  name: string
  description: string | null
  hourlyRate: number
  dailyRate: number
}

export type TenantKind = 'INDIVIDUAL' | 'LEGAL_ENTITY'
export interface TenantDto {
  id: string
  name: string
  kind: TenantKind
  isActive: boolean
  createdAt: string
  updatedAt: string
  concurrencyToken: string
}
export interface TenantInput { name: string, kind: TenantKind }

export type LeaseMode = 'MONTHLY' | 'DAILY' | 'HOURLY'
export type LeaseStatus = 'AGENDADA' | 'ATIVA' | 'ENCERRAMENTO_PENDENTE' | 'ENCERRADA' | 'CANCELADA'
export interface LeaseDto {
  id: string
  tenantId: string
  tenantName: string
  professionalId: string
  professionalName: string
  roomId: string
  roomName: string
  mode: LeaseMode
  contractedRate: number
  billingStartAt: string
  billingDueDay: number | null
  occupancyStartAt: string
  occupancyEndAt: string | null
  status: LeaseStatus
  createdAt: string
  updatedAt: string
  concurrencyToken: string
}
export interface LeaseInput {
  tenantId: string
  professionalId: string
  roomId: string
  mode: LeaseMode
  contractedRate: number
  billingStartAt: string
  billingDueDay: number | null
  occupancyStartAt: string
  occupancyEndAt: string | null
}
export interface LeaseListQuery {
  search?: string
  status: LeaseStatus | 'all'
  roomId?: string
  professionalId?: string
  tenantId?: string
  page: number
  pageSize: number
}
export interface ProfessionalLeaseDto {
  id: string
  tenantName: string
  roomId: string
  roomName: string
  mode: LeaseMode
  contractedRate: number
  billingStartAt: string
  billingDueDay: number | null
  occupancyStartAt: string
  occupancyEndAt: string | null
  status: LeaseStatus
}

export type ReservationKind = 'NEW' | 'RESCHEDULE' | 'CANCELLATION'
export type ReservationStatus = 'PENDING' | 'APPROVED' | 'REJECTED' | 'CANCELLED'
export interface ReservationDto {
  id: string
  roomId: string
  roomName: string
  professionalId: string
  professionalName: string
  originalReservationId: string | null
  kind: ReservationKind
  status: ReservationStatus
  startAt: string
  endAt: string
  requestedAt: string
  decidedAt: string | null
  rejectionReason: string | null
  createdAt: string
  updatedAt: string
  concurrencyToken: string
}
export interface ReservationPeriodInput { startAt: string, endAt: string }
export interface ReservationInput extends ReservationPeriodInput { roomId: string, professionalId: string }
export interface ProfessionalReservationInput extends ReservationPeriodInput { roomId: string }
export interface ReservationListQuery {
  status: ReservationStatus | 'all'
  roomId?: string
  professionalId?: string
  from?: string
  to?: string
  page: number
  pageSize: number
}

const professionalPath = (id: string) => `/api/admin/professionals/${encodeURIComponent(id)}`
const roomPath = (id: string) => `/api/admin/rooms/${encodeURIComponent(id)}`
const tenantPath = (id: string) => `/api/admin/tenants/${encodeURIComponent(id)}`
const leasePath = (id: string) => `/api/admin/leases/${encodeURIComponent(id)}`
const reservationPath = (id: string) => `/api/admin/reservations/${encodeURIComponent(id)}`
const professionalReservationPath = (id: string) => `/api/professional/reservations/${encodeURIComponent(id)}`

export const professionalsApi = {
  list(query: ModuleListQuery, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<ProfessionalDto>>('/api/admin/professionals', { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) {
    return apiClient.get<ProfessionalDto>(professionalPath(id), { signal })
  },
  create(input: ProfessionalInput) {
    return apiClient.post<ProfessionalDto>('/api/admin/professionals', input)
  },
  update(id: string, input: ProfessionalInput & { concurrencyToken: string }) {
    return apiClient.put<ProfessionalDto>(professionalPath(id), input)
  },
  changeStatus(id: string, active: boolean, concurrencyToken: string) {
    return apiClient.post<ProfessionalDto>(`${professionalPath(id)}/${active ? 'activate' : 'deactivate'}`,
      { concurrencyToken })
  },
  putPhoto(id: string, file: File, concurrencyToken: string) {
    const form = new FormData()
    form.append('concurrencyToken', concurrencyToken)
    form.append('file', file, file.name)
    return apiClient.putMultipart<ProfessionalDto>(`${professionalPath(id)}/photo`, form)
  },
  removePhoto(id: string, concurrencyToken: string) {
    return apiClient.delete<ProfessionalDto>(`${professionalPath(id)}/photo`, { concurrencyToken })
  },
  eligibleUsers(query: Omit<ModuleListQuery, 'status'>, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<EligibleUserDto>>('/api/admin/professionals/eligible-users',
      { query: { ...query }, signal })
  },
  userLink(id: string) {
    return apiClient.get<ProfessionalUserLinkDto>(`${professionalPath(id)}/user-link`)
  },
  putUserLink(id: string, applicationUserId: string, concurrencyToken: string) {
    return apiClient.put<ProfessionalDto>(`${professionalPath(id)}/user-link`,
      { applicationUserId, concurrencyToken })
  },
  removeUserLink(id: string, concurrencyToken: string) {
    return apiClient.delete<ProfessionalDto>(`${professionalPath(id)}/user-link`, { concurrencyToken })
  },
}

export const roomsApi = {
  list(query: ModuleListQuery, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<RoomDto>>('/api/admin/rooms', { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) {
    return apiClient.get<RoomDto>(roomPath(id), { signal })
  },
  create(input: RoomInput) {
    return apiClient.post<RoomDto>('/api/admin/rooms', input)
  },
  update(id: string, input: RoomInput & { concurrencyToken: string }) {
    return apiClient.put<RoomDto>(roomPath(id), input)
  },
  changeStatus(id: string, active: boolean, concurrencyToken: string) {
    return apiClient.post<RoomDto>(`${roomPath(id)}/${active ? 'activate' : 'deactivate'}`, { concurrencyToken })
  },
}

export const tenantsApi = {
  list(query: ModuleListQuery, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<TenantDto>>('/api/admin/tenants', { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) {
    return apiClient.get<TenantDto>(tenantPath(id), { signal })
  },
  create(input: TenantInput) {
    return apiClient.post<TenantDto>('/api/admin/tenants', input)
  },
  update(id: string, input: TenantInput & { concurrencyToken: string }) {
    return apiClient.put<TenantDto>(tenantPath(id), input)
  },
  changeStatus(id: string, active: boolean, concurrencyToken: string) {
    return apiClient.post<TenantDto>(`${tenantPath(id)}/${active ? 'activate' : 'deactivate'}`, { concurrencyToken })
  },
}

export const leasesApi = {
  list(query: LeaseListQuery, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<LeaseDto>>('/api/admin/leases', { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) {
    return apiClient.get<LeaseDto>(leasePath(id), { signal })
  },
  create(input: LeaseInput) {
    return apiClient.post<LeaseDto>('/api/admin/leases', input)
  },
  update(id: string, input: LeaseInput & { concurrencyToken: string }) {
    return apiClient.put<LeaseDto>(leasePath(id), input)
  },
  postpone(id: string, occupancyStartAt: string, concurrencyToken: string) {
    return apiClient.post<LeaseDto>(`${leasePath(id)}/postpone-occupancy`, { occupancyStartAt, concurrencyToken })
  },
  cancel(id: string, concurrencyToken: string) {
    return apiClient.post<LeaseDto>(`${leasePath(id)}/cancel`, { concurrencyToken })
  },
  end(id: string, endAt: string | null, concurrencyToken: string) {
    return apiClient.post<LeaseDto>(`${leasePath(id)}/end`, { endAt, concurrencyToken })
  },
}

export const professionalLeasesApi = {
  list(query: { page: number, pageSize: number }, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<ProfessionalLeaseDto>>('/api/professional/leases', { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) {
    return apiClient.get<ProfessionalLeaseDto>(`/api/professional/leases/${encodeURIComponent(id)}`, { signal })
  },
}

export const reservationsApi = {
  list(query: ReservationListQuery, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<ReservationDto>>('/api/admin/reservations', { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) {
    return apiClient.get<ReservationDto>(reservationPath(id), { signal })
  },
  create(input: ReservationInput) {
    return apiClient.post<ReservationDto>('/api/admin/reservations', input)
  },
  approve(id: string, concurrencyToken: string) {
    return apiClient.post<ReservationDto>(`${reservationPath(id)}/approve`, { concurrencyToken })
  },
  reject(id: string, reason: string, concurrencyToken: string) {
    return apiClient.post<ReservationDto>(`${reservationPath(id)}/reject`, { reason, concurrencyToken })
  },
  reschedule(id: string, input: ReservationPeriodInput & { concurrencyToken: string }) {
    return apiClient.post<ReservationDto>(`${reservationPath(id)}/reschedule`, input)
  },
  cancel(id: string, concurrencyToken: string) {
    return apiClient.post<ReservationDto>(`${reservationPath(id)}/cancel`, { concurrencyToken })
  },
}

export const professionalReservationsApi = {
  list(query: { status: ReservationStatus | 'all', page: number, pageSize: number }, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<ReservationDto>>('/api/professional/reservations',
      { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) {
    return apiClient.get<ReservationDto>(professionalReservationPath(id), { signal })
  },
  create(input: ProfessionalReservationInput) {
    return apiClient.post<ReservationDto>('/api/professional/reservations', input)
  },
  requestReschedule(id: string, input: ReservationPeriodInput & { concurrencyToken: string }) {
    return apiClient.post<ReservationDto>(`${professionalReservationPath(id)}/reschedule-request`, input)
  },
  requestCancellation(id: string, concurrencyToken: string) {
    return apiClient.post<ReservationDto>(`${professionalReservationPath(id)}/cancel-request`, { concurrencyToken })
  },
}
