namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.UnlockAccount;

/// <param name="UserId">
/// The account to release. Unguarded against naming the caller's own, for the reason the use case
/// gives.
/// </param>
public sealed record UnlockAccountCommand(Guid UserId);
