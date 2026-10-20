namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

public sealed record AccountIdentity(Guid UserId, string Email, bool TwoFactorEnabled);
