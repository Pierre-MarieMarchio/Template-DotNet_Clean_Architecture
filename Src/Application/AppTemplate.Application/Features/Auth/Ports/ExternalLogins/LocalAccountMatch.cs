using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.ExternalLogins;

/// <summary>A local account found by address, and the one further fact a linking decision needs.</summary>
/// <param name="EmailConfirmed">
/// Whether anyone ever proved they could read mail at that address. An account created through
/// registration and never confirmed has this <c>false</c>, and that is the case an automatic link
/// would hand to whoever registered it first.
/// </param>
public sealed record LocalAccountMatch(AccountIdentity Account, bool EmailConfirmed);
