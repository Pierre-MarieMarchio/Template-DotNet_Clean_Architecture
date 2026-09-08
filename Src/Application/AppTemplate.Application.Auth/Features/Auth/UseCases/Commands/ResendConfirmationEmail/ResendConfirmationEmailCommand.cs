namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResendConfirmationEmail;

/// <param name="Email">
/// The address to send another link to. Whether it is registered, and whether it is already
/// confirmed, is never answered back.
/// </param>
public sealed record ResendConfirmationEmailCommand(string Email);
