namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// Carries no user id: nothing in the rest of the sign-up journey addresses the account by id, and
/// the full profile is served by <c>GET /auth/me</c> once the caller holds a token.
/// <para>
/// <paramref name="ConfirmationEmailSent"/> is <c>false</c> when the account was created but the mail
/// could not be handed to the relay: point the user at the resend endpoint rather than treating it as
/// a failed sign-up.
/// </para>
/// </summary>
public sealed record RegisterResponse(string UserName, string Email, bool ConfirmationEmailSent);
