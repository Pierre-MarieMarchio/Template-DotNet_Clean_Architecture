namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.LockAccount;

/// <param name="UserId">
/// The account to suspend. Refused when it is the caller's own — see <c>SelfAdministrationPolicy</c>.
/// </param>
public sealed record LockAccountCommand(Guid UserId);
