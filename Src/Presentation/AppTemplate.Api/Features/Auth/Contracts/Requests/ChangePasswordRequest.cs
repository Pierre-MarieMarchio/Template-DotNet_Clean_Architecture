namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>Carries no identity: the account is the authenticated caller's own.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
