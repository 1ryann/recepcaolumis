// Single precedence for legacy multi-role accounts. This selects a home, not permissions.
export function homeForRoles(roles: string[]) {
  if (roles.includes('ADMINISTRADOR')) return '/admin'
  if (roles.includes('GERENTE')) return '/recepcao'
  if (roles.includes('PROFISSIONAL')) return '/profissional'
  if (roles.includes('CUSTOMER')) return '/cliente'
  if (roles.includes('PROFESSIONAL_APPLICANT')) return '/profissional/aguardando'
  return '/acesso-negado'
}
