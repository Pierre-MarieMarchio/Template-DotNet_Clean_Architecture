namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.Register;

/// <summary>
/// <paramref name="ConfirmationEmailSent"/> is <c>false</c> when the account was created but the
/// mail could not be handed to the relay: point the user at the resend endpoint rather than
/// treating it as a failed sign-up.
/// </summary>
public sealed record RegisterOutcome(Guid UserId, string Email, bool ConfirmationEmailSent);
