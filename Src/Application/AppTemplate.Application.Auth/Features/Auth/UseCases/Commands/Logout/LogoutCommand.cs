namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Logout;

/// <param name="RefreshToken">
/// The grant to revoke. Carried in the body rather than taken from the principal, so a client can
/// end one session without ending the rest.
/// </param>
public sealed record LogoutCommand(string RefreshToken);
