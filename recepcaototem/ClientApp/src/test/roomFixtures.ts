import type { PublicRoomCardDto, PublicRoomDetailDto, RoomDto, RoomFeatureFields } from '../api/modules'

// A room that exists but has not been described yet — no price, no measurements, no
// photos. That is the state every room starts in, so it is the right default for a
// fixture: a test that cares about a feature opts into it explicitly, and a test that
// does not stays readable instead of carrying seven nulls it never reads.

export const noRoomFeatures: RoomFeatureFields = {
  monthlyRate: null,
  areaSquareMeters: null,
  bathroomCount: null,
  capacityMin: null,
  capacityMax: null,
  category: null,
  amenities: [],
}

export function aRoomDto(overrides: Partial<RoomDto> = {}): RoomDto {
  return {
    id: 'room-1',
    name: 'Sala 1',
    description: null,
    hourlyRate: 50,
    dailyRate: 300,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    concurrencyToken: 'token',
    ...noRoomFeatures,
    ...overrides,
  }
}

export function aPublicRoomCard(overrides: Partial<PublicRoomCardDto> = {}): PublicRoomCardDto {
  return {
    id: 'room-1',
    name: 'Sala 1',
    description: null,
    availability: 'AVAILABLE_NOW',
    availableFrom: null,
    coverPhotoUrl: null,
    monthlyRate: null,
    capacityMin: null,
    capacityMax: null,
    category: null,
    ...overrides,
  }
}

export function aPublicRoomDetail(overrides: Partial<PublicRoomDetailDto> = {}): PublicRoomDetailDto {
  return {
    id: 'room-1',
    name: 'Sala 1',
    description: null,
    availability: 'AVAILABLE_NOW',
    availableFrom: null,
    photoUrls: [],
    monthlyRate: null,
    hourlyRate: null,
    dailyRate: null,
    areaSquareMeters: null,
    bathroomCount: null,
    capacityMin: null,
    capacityMax: null,
    category: null,
    amenities: [],
    whatsappUrl: null,
    ...overrides,
  }
}
