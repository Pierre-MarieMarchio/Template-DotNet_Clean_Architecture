namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// Both fields are optional, deliberately: which of them the account demands is the API's answer to
/// say — the current password when the account has one, a two-factor code when the factor is armed,
/// and neither when the account is provider-linked, in which case the freshly issued access token
/// making the call is the proof. The response names the field that was missing, so a client knows
/// what to ask for.
/// </summary>
public sealed record DeleteMyAccountRequest(string? CurrentPassword, string? TwoFactorCode);
