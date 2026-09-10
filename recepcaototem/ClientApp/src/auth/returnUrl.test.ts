import { describe, expect, it } from 'vitest'
import { safeCustomerReturnUrl } from './returnUrl'

describe('safeCustomerReturnUrl — rejects', () => {
  const bad: [string, string | null | undefined][] = [
    ['null', null], ['undefined', undefined], ['empty', ''],
    ['https', 'https://evil.com'], ['http', 'http://evil.com'], ['HTTP upper', 'HTTP://evil.com'],
    ['javascript', 'javascript:alert(1)'], ['data', 'data:text/html,x'],
    ['protocol-relative', '//evil.com'], ['slash-backslash', '/\\evil.com'],
    ['bare backslash', '\\evil'], ['backslash after cliente', '/cliente\\evil'],
    ['encoded //', '%2F%2Fevil.com'], ['double-encoded //', '%252F%252Fevil.com'],
    ['encoded scheme', '%68ttp://evil'],
    ['traversal', '/cliente/../admin'], ['encoded traversal', '/cliente/%2e%2e/admin'],
    ['admin', '/admin'], ['recepcao', '/recepcao'], ['root', '/'], ['login', '/login'],
    ['cliente prefix trick', '/cliente-admin'], ['clientefoo', '/clientefoo'],
    ['script in query', '/cliente/agendar?x=<script>'],
    ['two question marks', '/cliente/agendar?a=b?c=d'],
    ['control char (NUL)', '/cliente/age' + String.fromCharCode(0) + 'nda'],
    ['too long', '/cliente/' + 'a'.repeat(600)],
  ]
  it.each(bad)('%s', (_label, value) => expect(safeCustomerReturnUrl(value)).toBeNull())
})

describe('safeCustomerReturnUrl — accepts (normalized)', () => {
  it.each([
    ['/cliente', '/cliente'],
    ['/cliente/agendar', '/cliente/agendar'],
    ['/cliente/agendamentos', '/cliente/agendamentos'],
    ['/cliente/agendamentos/abc', '/cliente/agendamentos/abc'],
    ['/cliente/agendar?professionalId=8f3c1e2a-0000-4a00-8000-000000000001',
     '/cliente/agendar?professionalId=8f3c1e2a-0000-4a00-8000-000000000001'],
    ['%2Fcliente%2Fagendar%3FprofessionalId%3D8f3c1e2a-0000-4a00-8000-000000000001',
     '/cliente/agendar?professionalId=8f3c1e2a-0000-4a00-8000-000000000001'],
  ])('%s', (input, expected) => expect(safeCustomerReturnUrl(input)).toBe(expected))
})
