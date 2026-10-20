namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// The proof the account demands: its current password, a two-factor code, or neither when
/// provider-linked.
/// </summary>
public sealed record DeleteMyAccountRequest(string? CurrentPassword, string? TwoFactorCode);
