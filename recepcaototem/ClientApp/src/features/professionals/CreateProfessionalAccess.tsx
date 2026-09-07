import { useState } from 'react'
import { apiClient } from '../../api/client'
export function CreateProfessionalAccess({ name, onCreated }: { name: string; onCreated(userId: string): void }) {
 const [email, setEmail] = useState('')
 const [pending, setPending] = useState(false)
 const [created, setCreated] = useState<{ userId: string; temporaryPassword: string } | null>(null)
 const [error, setError] = useState('')
 const create = async () => {
  setPending(true); setError('')
  try {
   const account = await apiClient.post<{ userId: string; temporaryPassword: string }>('/api/admin/users', { displayName: name, email: email.trim(), role: 'PROFISSIONAL' })
   setCreated(account); onCreated(account.userId)
  } catch { setError('Não foi possível criar o acesso. Confira o e-mail antes de tentar novamente.') }
  finally { setPending(false) }
 }
 return <section>{created ? <div role="status"><p>Conta criada. Guarde a senha temporária e confirme o vínculo abaixo. A troca será obrigatória no primeiro acesso.</p><code>{created.temporaryPassword}</code><p>A senha desaparece ao fechar esta janela.</p></div> : <><label className="field-label">E-mail da nova conta<input className="field-input" type="email" value={email} onChange={event => setEmail(event.target.value)} /></label><button className="secondary-button" type="button" disabled={pending || !email.trim()} onClick={() => void create()}>Criar acesso profissional</button></>}{error && <p role="alert">{error}</p>}</section>
}
