import { afterEach, expect, test, vi } from 'vitest'
import { apiClient } from './client'
import { professionalFinanceApi } from './modules'

afterEach(() => vi.restoreAllMocks())

test('professionalFinanceApi.list GETs /api/professional/finance/charges with filters', async () => {
  const get = vi.spyOn(apiClient, 'get').mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 } as never)
  await professionalFinanceApi.list({ status: 'all', page: 1, pageSize: 20 })
  expect(get).toHaveBeenCalledWith('/api/professional/finance/charges', { query: { status: 'all', page: 1, pageSize: 20 }, signal: undefined })
})
