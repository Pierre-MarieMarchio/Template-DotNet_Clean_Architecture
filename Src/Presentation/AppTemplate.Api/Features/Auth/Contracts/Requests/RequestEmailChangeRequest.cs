namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>Carries no identity beyond the new address: the account being changed is the authenticated caller's own.</summary>
public sealed record RequestEmailChangeRequest(string CurrentPassword, string NewEmail);
