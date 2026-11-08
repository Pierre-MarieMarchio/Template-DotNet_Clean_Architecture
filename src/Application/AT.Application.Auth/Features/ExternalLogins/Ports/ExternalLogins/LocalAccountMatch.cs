using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;

public sealed record LocalAccountMatch(AccountIdentity Account, bool EmailConfirmed);
