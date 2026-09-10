// Elegant fallback for a missing professional photo: 1–2 uppercase letters.
export function professionalInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean).filter(p => !p.includes('.'))
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].charAt(0).toUpperCase()
  return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase()
}
