namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DisableAccountTwoFactor;

/// <param name="UserId">
/// The account to strip. Refused when it is the caller's own, for the reason
/// <c>DisableAccountTwoFactorUseCase</c> sets out.
/// </param>
public sealed record DisableAccountTwoFactorCommand(Guid UserId);
