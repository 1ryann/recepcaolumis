import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import { ApiError } from '../../api/client'
import { customersAdministrationApi } from '../../api/modules'
import { Customers } from './Customers'

vi.mock('../../api/modules', () => ({
  customersAdministrationApi: { list: vi.fn(), changeStatus: vi.fn(), remove: vi.fn() },
}))

const customer = {
  id: 'customer-1', name: 'Maria Clara Souza', phone: '+5569999538007', isActive: true, hasAccount: false,
  whatsAppOptIn: { status: 'GRANTED' as const, changedAt: '2026-09-20T00:00:00Z', source: 'CUSTOMER_REGISTRATION', textVersion: 'v1' },
  createdAt: '2026-09-20T00:00:00Z', concurrencyToken: 'token-1',
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(customersAdministrationApi.list).mockResolvedValue({ items: [customer], page: 1, pageSize: 20, totalCount: 1 })
})

test('lists the customers the server returns, with the phone and opt-in readable', async () => {
  render(<Customers />)

  expect(await screen.findByText('Maria Clara Souza')).toBeInTheDocument()
  expect(screen.getByText('(69) 99953-8007')).toBeInTheDocument()
  expect(screen.getByText('Aceita avisos')).toBeInTheDocument()
  expect(customersAdministrationApi.list).toHaveBeenCalledWith(
    { search: undefined, status: 'all', page: 1, pageSize: 20 }, expect.any(AbortSignal))
})

test('deactivating a customer sends its token and shows the new status', async () => {
  vi.mocked(customersAdministrationApi.changeStatus).mockResolvedValue({ ...customer, isActive: false, concurrencyToken: 'token-2' })
  render(<Customers />)

  fireEvent.click(await screen.findByLabelText('Desativar Maria Clara Souza'))

  await waitFor(() => expect(customersAdministrationApi.changeStatus).toHaveBeenCalledWith('customer-1', false, 'token-1'))
  expect(await screen.findByLabelText('Ativar Maria Clara Souza')).toBeInTheDocument()
})

test('a record changed elsewhere reloads the list and says so instead of failing silently', async () => {
  vi.mocked(customersAdministrationApi.changeStatus)
    .mockRejectedValue(new ApiError(409, 'RESOURCE_MODIFIED', 'O registro foi alterado por outra operação.'))
  render(<Customers />)

  fireEvent.click(await screen.findByLabelText('Desativar Maria Clara Souza'))

  expect(await screen.findByRole('alert')).toHaveTextContent('alterado por outra operação')
  await waitFor(() => expect(customersAdministrationApi.list).toHaveBeenCalledTimes(2))
})

test('the search is debounced into the server query and the empty result is the server\'s', async () => {
  vi.mocked(customersAdministrationApi.list)
    .mockResolvedValueOnce({ items: [customer], page: 1, pageSize: 20, totalCount: 1 })
    .mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  render(<Customers />)
  fireEvent.change(await screen.findByLabelText('Buscar clientes'), { target: { value: '  69999538007  ' } })

  await waitFor(() => expect(customersAdministrationApi.list).toHaveBeenCalledWith(
    { search: '69999538007', status: 'all', page: 1, pageSize: 20 }, expect.any(AbortSignal)))
  expect(await screen.findByText('Nenhum cliente encontrado.')).toBeInTheDocument()
})

test('deleting asks for confirmation first and reports that the record was removed', async () => {
  vi.mocked(customersAdministrationApi.remove).mockResolvedValue({ outcome: 'DELETED' })
  render(<Customers />)

  fireEvent.click(await screen.findByRole('button', { name: 'Excluir Maria Clara Souza' }))
  // Nothing leaves the screen before the confirmation is accepted.
  expect(customersAdministrationApi.remove).not.toHaveBeenCalled()

  fireEvent.click(screen.getByRole('button', { name: 'Excluir definitivamente' }))
  await waitFor(() => expect(customersAdministrationApi.remove).toHaveBeenCalledWith('customer-1', 'token-1'))
  expect(await screen.findByText(/foi excluído/)).toBeInTheDocument()
})

test('a customer with history is reported as anonymised, not as deleted', async () => {
  vi.mocked(customersAdministrationApi.remove).mockResolvedValue({ outcome: 'ANONYMIZED' })
  render(<Customers />)

  fireEvent.click(await screen.findByRole('button', { name: 'Excluir Maria Clara Souza' }))
  fireEvent.click(screen.getByRole('button', { name: 'Excluir definitivamente' }))

  expect(await screen.findByText(/histórico foi mantido sem identificação/)).toBeInTheDocument()
})
