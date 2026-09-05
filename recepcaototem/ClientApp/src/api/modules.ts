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

const professionalPath = (id: string) => `/api/admin/professionals/${encodeURIComponent(id)}`
const roomPath = (id: string) => `/api/admin/rooms/${encodeURIComponent(id)}`

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
