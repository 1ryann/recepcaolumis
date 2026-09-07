namespace GestaoPredio.Domain.Security;
public static class SystemRoles {
 public const string Administrador = "ADMINISTRADOR";
 public const string Gerente = "GERENTE";
 public const string Profissional = "PROFISSIONAL";
 public const string Customer = "CUSTOMER";
 public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
 {
  Administrador, Gerente, Profissional
 };
 public static readonly IReadOnlySet<string> AuthenticationRoles = new HashSet<string>(All.Append(Customer), StringComparer.Ordinal);
}
