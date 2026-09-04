namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// Carries no identity: the pending secret is the authenticated caller's own. <c>CurrentPassword</c>
/// is required for the same reason <c>DisableTwoFactorRequest.CurrentPassword</c> is: arming the
/// second factor is as much a security-posture change as disarming it.
/// </summary>
public sealed record ConfirmTwoFactorSetupRequest(string CurrentPassword, string Code);
