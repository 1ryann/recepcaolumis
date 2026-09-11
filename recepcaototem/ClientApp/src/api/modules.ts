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

export interface CustomerProfileDto {
  id: string
  name: string
  phone: string
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface CustomerProfessionalDto {
  id: string
  name: string
  profession: string
  description: string | null
}

export interface CustomerRegisterInput {
  name: string
  phone: string
  email: string
  password: string
  confirmation: string
}

export type ProfessionalApplicationStatus = 'PENDING' | 'APPROVED' | 'REJECTED'
export interface ProfessionalApplicationDto {
  id: string
  name: string
  profession: string
  description: string | null
  status: ProfessionalApplicationStatus
  createdAt: string
  reviewedAt: string | null
  concurrencyToken: string
}
export interface ProfessionalRegistrationInput {
  name: string
  profession: string
  whatsApp: string
  email: string
  password: string
  confirmation: string
  description: string | null
}

export interface AvailabilitySlotDto {
  startAt: string
  endAt: string
}

export type ProfessionalAvailabilityMode = 'INHERIT_GLOBAL' | 'CUSTOM'
export interface AvailabilityIntervalDto { startTime: string, endTime: string }
export interface AvailabilityDayDto {
  dayOfWeek: string
  intervals: AvailabilityIntervalDto[]
  effectiveIntervals?: AvailabilityIntervalDto[]
}
export interface ProfessionalAvailabilityDto {
  mode: ProfessionalAvailabilityMode
  days: AvailabilityDayDto[]
  effectiveDays: AvailabilityDayDto[]
  globalDays: AvailabilityDayDto[]
  concurrencyToken: string
  existingReservationsOutsideAvailabilityCount: number
}
export interface ProfessionalAvailabilityUpdateInput {
  mode: ProfessionalAvailabilityMode
  days?: Array<{ dayOfWeek: string, intervals: AvailabilityIntervalDto[] }>
  concurrencyToken?: string | null
}
export interface AvailabilityExceptionDto {
  id: string
  date: string
  allDay: boolean
  startTime: string | null
  endTime: string | null
  reason: string | null
  createdAt: string
  updatedAt: string
  concurrencyToken: string
  existingReservationsOutsideAvailabilityCount?: number
}
export interface AvailabilityExceptionInput {
  date: string
  allDay: boolean
  startTime: string | null
  endTime: string | null
  reason: string | null
  concurrencyToken?: string | null
}
export interface OperatingHoursDayDto {
  dayOfWeek: string
  intervals: Array<{ opensAt: string, closesAt: string }>
}
export interface OperatingHoursDto {
  configured: boolean
  days: OperatingHoursDayDto[]
  concurrencyToken: string | null
}
export interface OperatingHoursUpdateInput {
  days: OperatingHoursDayDto[]
  concurrencyToken?: string | null
}
export type RoomBlockStatus = 'ACTIVE' | 'CANCELLED'
export interface RoomBlockDto {
  id: string
  roomId: string
  roomName: string
  startAt: string
  endAt: string
  reason: string
  status: RoomBlockStatus
  createdAt: string
  concurrencyToken: string
}
export interface RoomBlockQuery {
  page: number
  pageSize: number
  status?: RoomBlockStatus | 'all'
  roomId?: string
  from?: string
  to?: string
}
export interface RoomBlockCreateInput { roomId: string, startAt: string, endAt: string, reason: string }
export interface RoomBlockUpdateInput { startAt: string, endAt: string, reason: string, concurrencyToken: string }

export interface TotemProfessionalCardDto {
  id: string
  name: string
  profession: string
  photoUrl: string | null
  status: 'AVAILABLE' | 'IN_SERVICE' | 'UNAVAILABLE'
}

export interface CreateHandoffDto {
  id: string
  handoffToken: string
  statusToken: string
  expiresAt: string
  professionalName: string
  profession: string
}

export type HandoffStatusDto =
  | { status: 'PENDING'; expiresAt: string }
  | { status: 'COMPLETED'; professionalName: string; startAt: string; roomName: string | null }
  | { status: 'EXPIRED' }

export interface ResolveHandoffDto {
  handoffId: string
  professionalId: string
  professionalName: string
  profession: string
  expiresAt: string
}

export interface CheckInPreviewDto {
  professional: string
  room: string
  startAt: string
  endAt: string
  eligible: boolean
}

export interface CheckInResultDto {
  visitId: string
  status: VisitStatus
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

export type VisitStatus = 'WAITING' | 'IN_SERVICE' | 'ENDED' | 'CANCELLED'
export interface VisitTransitionDto { id: string, previousStatus: VisitStatus | null, newStatus: VisitStatus, occurredAt: string, reason: string | null, isCorrection: boolean }
export interface VisitDto {
  id: string; professionalId: string; professionalName: string; roomId: string | null; roomName: string | null
  reservationId: string | null; visitorName: string; status: VisitStatus; arrivedAt: string
  serviceStartedAt: string | null; endedAt: string | null; cancelledAt: string | null
  createdAt: string; updatedAt: string; concurrencyToken: string; history: VisitTransitionDto[]
}
export interface VisitListQuery {
  status: VisitStatus | 'all'; professionalId?: string; roomId?: string; from?: string; to?: string
  page: number; pageSize: number
}
export interface VisitInput { professionalId: string, roomId: string | null, reservationId: string | null, visitorName: string }

export interface ReceptionVisitDto {
  id: string
  professionalId: string
  professionalName: string
  roomId: string | null
  roomName: string | null
  reservationId: string | null
  customerId: string | null
  customerName: string | null
  visitorName: string
  status: VisitStatus
  arrivedAt: string
  serviceStartedAt: string | null
  endedAt: string | null
  concurrencyToken: string
}

export interface ReceptionOverviewDto {
  visitorsWaiting: number
  visitsInService: number
  professionalsAvailable: number
  professionalsInService: number
  roomsAvailable: number
  roomsOccupied: number
  reservationsToday: number
  upcomingReservations: unknown[]
  waitingVisits: ReceptionVisitDto[]
  currentVisits: ReceptionVisitDto[]
  warningAlerts: number
  criticalAlerts: number
}

export interface DashboardCountsDto {
  activeProfessionals: number
  activeRooms: number
  occupiedRooms: number
  reservedRooms: number
  activeLeases: number
  scheduledLeases: number
  pendingReservations: number
  todayReservations: number
  waitingVisits: number
  inServiceVisits: number
  todayCheckIns: number
}

export interface DashboardFinancialSummaryDto {
  pendingAmount: number
  overdueAmount: number
  paidAmount: number
  pendingCount: number
  overdueCount: number
  paidCount: number
}

export interface DashboardAgendaItemDto {
  reservationId: string
  professionalId: string
  professionalName: string
  roomId: string
  roomName: string
  startAt: string
  endAt: string
}

export interface DashboardCurrentVisitDto {
  visitId: string
  visitorName: string
  status: string
  professionalId: string
  professionalName: string
  roomId: string | null
  roomName: string | null
  arrivedAt: string
  serviceStartedAt: string | null
  durationMinutes: number
}

export interface DashboardAlertDto {
  id: string
  type: string
  severity: string
  title: string
  message: string
  conditionAt: string
  roomId: string | null
  professionalId: string | null
  reservationId: string | null
  visitId: string | null
  leaseId: string | null
}

export interface DashboardAlertSummaryDto {
  total: number
  warning: number
  critical: number
  recent: DashboardAlertDto[]
}

export interface DashboardRoomStatusDto {
  roomId: string
  roomName: string
  status: string
  nextCommitmentAt: string | null
}

export interface DashboardSnapshotDto {
  operationalDate: string
  counts: DashboardCountsDto
  financial: DashboardFinancialSummaryDto
  alerts: DashboardAlertSummaryDto
  agenda: DashboardAgendaItemDto[]
  currentVisits: DashboardCurrentVisitDto[]
  rooms: DashboardRoomStatusDto[]
}

export interface ReceptionProfessionalDto {
  professionalId: string
  name: string
  profession: string
  description: string | null
  hasPhoto: boolean
  photoUrl: string | null
  operationalStatus: string
  currentRoomId: string | null
  currentVisitId: string | null
  waitingVisitorsCount: number
  nextReservationAt: string | null
  canReceiveVisitor: boolean
  presence: 'PRESENT' | 'ABSENT'
  absentUntil: string | null
}

export const dashboardApi = {
  get(signal?: AbortSignal) {
    return apiClient.get<DashboardSnapshotDto>('/api/admin/dashboard', { signal })
  },
}

const professionalPath = (id: string) => `/api/admin/professionals/${encodeURIComponent(id)}`
const roomPath = (id: string) => `/api/admin/rooms/${encodeURIComponent(id)}`
const tenantPath = (id: string) => `/api/admin/tenants/${encodeURIComponent(id)}`
const leasePath = (id: string) => `/api/admin/leases/${encodeURIComponent(id)}`
const reservationPath = (id: string) => `/api/admin/reservations/${encodeURIComponent(id)}`
const professionalReservationPath = (id: string) => `/api/professional/reservations/${encodeURIComponent(id)}`
const visitPath = (id: string) => `/api/admin/visits/${encodeURIComponent(id)}`
const professionalVisitPath = (id: string) => `/api/professional/visits/${encodeURIComponent(id)}`

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
  list(
    query: { status: ReservationStatus | 'all', page: number, pageSize: number, from?: string, to?: string },
    signal?: AbortSignal,
  ) {
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

export const visitsApi = {
  list(query: VisitListQuery, signal?: AbortSignal) { return apiClient.get<PagedResponse<VisitDto>>('/api/admin/visits', { query: { ...query }, signal }) },
  detail(id: string, signal?: AbortSignal) { return apiClient.get<VisitDto>(visitPath(id), { signal }) },
  create(input: VisitInput) { return apiClient.post<VisitDto>('/api/admin/visits', input) },
  start(id: string, concurrencyToken: string) { return apiClient.post<VisitDto>(`${visitPath(id)}/start`, { concurrencyToken }) },
  end(id: string, concurrencyToken: string) { return apiClient.post<VisitDto>(`${visitPath(id)}/end`, { concurrencyToken }) },
  cancel(id: string, concurrencyToken: string) { return apiClient.post<VisitDto>(`${visitPath(id)}/cancel`, { concurrencyToken }) },
  correct(id: string, status: VisitStatus, reason: string, concurrencyToken: string) {
    return apiClient.post<VisitDto>(`${visitPath(id)}/correct`, { status, reason, concurrencyToken })
  },
}

export const professionalVisitsApi = {
  list(query: Pick<VisitListQuery, 'status' | 'from' | 'to' | 'page' | 'pageSize'>, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<VisitDto>>('/api/professional/visits', { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) { return apiClient.get<VisitDto>(professionalVisitPath(id), { signal }) },
  start(id: string, concurrencyToken: string) { return apiClient.post<VisitDto>(`${professionalVisitPath(id)}/start`, { concurrencyToken }) },
  end(id: string, concurrencyToken: string) { return apiClient.post<VisitDto>(`${professionalVisitPath(id)}/end`, { concurrencyToken }) },
  cancel(id: string, concurrencyToken: string) { return apiClient.post<VisitDto>(`${professionalVisitPath(id)}/cancel`, { concurrencyToken }) },
}

export const customerApi = {
  register(input: CustomerRegisterInput) {
    return apiClient.post<CustomerProfileDto>('/api/customer/register', input)
  },
  me(signal?: AbortSignal) {
    return apiClient.get<CustomerProfileDto>('/api/customer/me', { signal })
  },
  professionals(signal?: AbortSignal) {
    return apiClient.get<CustomerProfessionalDto[]>('/api/customer/professionals', { signal })
  },
  availability(query: { professionalId: string, date: string, durationMinutes: number }, signal?: AbortSignal) {
    return apiClient.get<AvailabilitySlotDto[]>('/api/customer/availability', { query, signal })
  },
  reservations(query: { page: number, pageSize: number }, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<ReservationDto>>('/api/customer/reservations', { query, signal })
  },
  reservation(id: string, signal?: AbortSignal) {
    return apiClient.get<ReservationDto>(`/api/customer/reservations/${encodeURIComponent(id)}`, { signal })
  },
  createReservation(input: { professionalId: string, startAt: string, endAt: string, handoffToken?: string }) {
    return apiClient.post<ReservationDto>('/api/customer/reservations', input)
  },
  resolveHandoff(handoffToken: string) {
    return apiClient.post<ResolveHandoffDto>('/api/customer/booking-handoffs/resolve', { handoffToken })
  },
  issueCheckInToken(id: string) {
    return apiClient.post<{ token: string, manualCode: string, expiresAt: string }>(`/api/customer/reservations/${encodeURIComponent(id)}/check-in-token`, {})
  },
}

const adminProfessionalAvailabilityPath = (professionalId: string) =>
  `/api/admin/professionals/${encodeURIComponent(professionalId)}/availability`
const professionalAvailabilityExceptionsPath = '/api/professional/availability/exceptions'
const adminProfessionalAvailabilityExceptionsPath = (professionalId: string) =>
  `${adminProfessionalAvailabilityPath(professionalId)}/exceptions`

export interface AvailabilityExceptionRange { from: string, to: string }

// The exceptions endpoint rejects any request without an explicit from/to window of
// at most 365 days (INVALID_DATE_RANGE). Default to "today through the next year" so
// the editor always loads its upcoming entries instead of failing on open.
function defaultExceptionRange(): AvailabilityExceptionRange {
  const iso = (value: Date) =>
    `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`
  const from = new Date()
  const to = new Date(from)
  to.setDate(to.getDate() + 365)
  return { from: iso(from), to: iso(to) }
}

export const professionalAvailabilityApi = {
  get(signal?: AbortSignal) {
    return apiClient.get<ProfessionalAvailabilityDto>('/api/professional/availability', { signal })
  },
  update(input: ProfessionalAvailabilityUpdateInput) {
    return apiClient.put<ProfessionalAvailabilityDto>('/api/professional/availability', input)
  },
  listExceptions(signal?: AbortSignal, range: AvailabilityExceptionRange = defaultExceptionRange()) {
    return apiClient.get<AvailabilityExceptionDto[]>(professionalAvailabilityExceptionsPath, { query: { ...range }, signal })
  },
  createException(input: AvailabilityExceptionInput) {
    return apiClient.post<AvailabilityExceptionDto>(professionalAvailabilityExceptionsPath, input)
  },
  updateException(id: string, input: AvailabilityExceptionInput & { concurrencyToken: string }) {
    return apiClient.put<AvailabilityExceptionDto>(`${professionalAvailabilityExceptionsPath}/${encodeURIComponent(id)}`, input)
  },
  deleteException(id: string, concurrencyToken: string) {
    return apiClient.delete<AvailabilityExceptionDto>(`${professionalAvailabilityExceptionsPath}/${encodeURIComponent(id)}`, { concurrencyToken })
  },
}

export const adminProfessionalAvailabilityApi = {
  get(professionalId: string, signal?: AbortSignal) {
    return apiClient.get<ProfessionalAvailabilityDto>(adminProfessionalAvailabilityPath(professionalId), { signal })
  },
  update(professionalId: string, input: ProfessionalAvailabilityUpdateInput) {
    return apiClient.put<ProfessionalAvailabilityDto>(adminProfessionalAvailabilityPath(professionalId), input)
  },
  listExceptions(professionalId: string, signal?: AbortSignal, range: AvailabilityExceptionRange = defaultExceptionRange()) {
    return apiClient.get<AvailabilityExceptionDto[]>(adminProfessionalAvailabilityExceptionsPath(professionalId), { query: { ...range }, signal })
  },
  createException(professionalId: string, input: AvailabilityExceptionInput) {
    return apiClient.post<AvailabilityExceptionDto>(adminProfessionalAvailabilityExceptionsPath(professionalId), input)
  },
  updateException(professionalId: string, id: string, input: AvailabilityExceptionInput & { concurrencyToken: string }) {
    return apiClient.put<AvailabilityExceptionDto>(`${adminProfessionalAvailabilityExceptionsPath(professionalId)}/${encodeURIComponent(id)}`, input)
  },
  deleteException(professionalId: string, id: string, concurrencyToken: string) {
    return apiClient.delete<AvailabilityExceptionDto>(`${adminProfessionalAvailabilityExceptionsPath(professionalId)}/${encodeURIComponent(id)}`, { concurrencyToken })
  },
}

export const operatingHoursApi = {
  get(signal?: AbortSignal) {
    return apiClient.get<OperatingHoursDto>('/api/admin/operating-hours', { signal })
  },
  update(input: OperatingHoursUpdateInput) {
    return apiClient.put<OperatingHoursDto>('/api/admin/operating-hours', input)
  },
}

export const roomBlocksApi = {
  list(query: RoomBlockQuery, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<RoomBlockDto>>('/api/admin/room-blocks', { query: { ...query }, signal })
  },
  detail(id: string, signal?: AbortSignal) {
    return apiClient.get<RoomBlockDto>(`/api/admin/room-blocks/${encodeURIComponent(id)}`, { signal })
  },
  create(input: RoomBlockCreateInput) {
    return apiClient.post<RoomBlockDto>('/api/admin/room-blocks', input)
  },
  update(id: string, input: RoomBlockUpdateInput) {
    return apiClient.put<RoomBlockDto>(`/api/admin/room-blocks/${encodeURIComponent(id)}`, input)
  },
  cancel(id: string, concurrencyToken: string) {
    return apiClient.post<RoomBlockDto>(`/api/admin/room-blocks/${encodeURIComponent(id)}/cancel`, { concurrencyToken })
  },
}

export const totemApi = {
  professionals(signal?: AbortSignal) {
    return apiClient.get<TotemProfessionalCardDto[]>('/api/totem/professionals', { signal })
  },
  resolveCheckIn(token: string) {
    return apiClient.post<CheckInPreviewDto>('/api/totem/check-in/resolve', { token })
  },
  confirmCheckIn(token: string) {
    return apiClient.post<CheckInResultDto>('/api/totem/check-in/confirm', { token })
  },
  createHandoff(professionalId: string) {
    return apiClient.post<CreateHandoffDto>('/api/totem/booking-handoffs', { professionalId })
  },
  pollHandoff(id: string, statusToken: string) {
    return apiClient.post<HandoffStatusDto>(`/api/totem/booking-handoffs/${id}/status`, { statusToken })
  },
  cancelHandoff(id: string, statusToken: string) {
    return apiClient.post<{ status: string }>(`/api/totem/booking-handoffs/${id}/cancel`, { statusToken })
  },
  claimHandoff(handoffToken: string) {
    return apiClient.post<{ status: string, expiresAt: string }>('/api/totem/booking-handoffs/claim', { handoffToken })
  },
}

export const professionalRegistrationApi = {
  register(input: ProfessionalRegistrationInput) {
    return apiClient.post<ProfessionalApplicationDto>('/api/professional-registration/register', input)
  },
  me(signal?: AbortSignal) {
    return apiClient.get<ProfessionalApplicationDto>('/api/professional-registration/me', { signal })
  },
  list(query: { status?: ProfessionalApplicationStatus | 'all', page?: number, pageSize?: number } = {}) {
    return apiClient.get<PagedResponse<ProfessionalApplicationDto>>('/api/reception/professional-applications', { query })
  },
  approve(id: string, concurrencyToken: string) {
    return apiClient.post<ProfessionalApplicationDto>(`/api/reception/professional-applications/${encodeURIComponent(id)}/approve`, { concurrencyToken })
  },
  reject(id: string, concurrencyToken: string) {
    return apiClient.post<ProfessionalApplicationDto>(`/api/reception/professional-applications/${encodeURIComponent(id)}/reject`, { concurrencyToken })
  },
}

export const receptionApi = {
  overview(signal?: AbortSignal) {
    return apiClient.get<ReceptionOverviewDto>('/api/reception/overview', { signal })
  },
  visits(query: { status?: VisitStatus | 'all', page: number, pageSize: number }, signal?: AbortSignal) {
    return apiClient.get<PagedResponse<ReceptionVisitDto>>('/api/reception/visits', { query, signal })
  },
  startVisit(id: string, concurrencyToken: string) {
    return apiClient.post<ReceptionVisitDto>(`/api/reception/visits/${encodeURIComponent(id)}/start`, { concurrencyToken })
  },
  endVisit(id: string, concurrencyToken: string) {
    return apiClient.post<ReceptionVisitDto>(`/api/reception/visits/${encodeURIComponent(id)}/end`, { concurrencyToken })
  },
  professionals(signal?: AbortSignal) {
    return apiClient.get<ReceptionProfessionalDto[]>('/api/reception/professionals', { signal })
  },
}
