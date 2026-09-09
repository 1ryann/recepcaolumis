import { useState } from 'react'
import { apiClient } from '../../api/client'

// Administrative emergency reset for an already-linked professional account. The temporary
// password lives only in this component's state: it is shown once, never persisted, and
// discarded when the dialog closes. It is not the primary flow — self-service by e-mail
// will come later.
export function ResetProfessionalPassword({ userId }: { userId: string }) {
 const [confirming, setConfirming] = useState(false)
 const [pending, setPending] = useState(false)
 const [temporaryPassword, setTemporaryPassword] = useState<string | null>(null)
 const [error, setError] = useState('')
 const reset = async () => {
  setPending(true); setError('')
  try {
   const result = await apiClient.post<{ userId: string; temporaryPassword: string }>(`/api/admin/users/${userId}/reset-password`, {})
   setTemporaryPassword(result.temporaryPassword); setConfirming(false)
  } catch { setError('Não foi possível redefinir a senha. Tente novamente.'); setConfirming(false) }
  finally { setPending(false) }
 }
 if (temporaryPassword) {
  return <div role="status" className="reset-password-result">
   <p>Anote esta senha agora. Ela não será exibida novamente. O usuário será obrigado a criar uma nova senha no próximo acesso.</p>
   <code>{temporaryPassword}</code>
   <button className="secondary-button" type="button" onClick={() => setTemporaryPassword(null)}>Fechar</button>
  </div>
 }
 return <div className="reset-password">
  {confirming
   ? <><p>Tem certeza que deseja redefinir a senha deste usuário?</p>
     <button className="danger-button" type="button" disabled={pending} onClick={() => void reset()}>Confirmar redefinição</button>
     <button className="secondary-button" type="button" disabled={pending} onClick={() => setConfirming(false)}>Cancelar</button></>
   : <button className="secondary-button" type="button" onClick={() => { setError(''); setConfirming(true) }}>Redefinir senha</button>}
  {error && <p role="alert">{error}</p>}
 </div>
}
