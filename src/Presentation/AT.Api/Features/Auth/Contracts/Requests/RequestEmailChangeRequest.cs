namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>The new address and the current password of the authenticated caller's own account.</summary>
public sealed record RequestEmailChangeRequest(string CurrentPassword, string NewEmail);
