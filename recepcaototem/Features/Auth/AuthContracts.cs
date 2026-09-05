namespace recepcaototem.Features.Auth;

public sealed record LoginRequest(string Email, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string Confirmation);
public sealed record CsrfResponse(string Token);
public sealed record SessionResponse(string UserId, string DisplayName, string Email, string[] Roles, bool MustChangePassword);
