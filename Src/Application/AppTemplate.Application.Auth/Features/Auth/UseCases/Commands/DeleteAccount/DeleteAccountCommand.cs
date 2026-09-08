namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteAccount;

/// <param name="UserId">
/// The account to remove. Refused when it is the caller's own — see <c>SelfAdministrationPolicy</c>.
/// </param>
public sealed record DeleteAccountCommand(Guid UserId);
