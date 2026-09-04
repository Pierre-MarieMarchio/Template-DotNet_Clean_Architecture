namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DisableTwoFactor;

/// <summary>Carries no identity: the account is the authenticated caller's own.</summary>
public sealed record DisableTwoFactorCommand(string CurrentPassword);
