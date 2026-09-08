namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RequestEmailChange;

/// <param name="CurrentPassword">
/// Proof the request comes from the holder rather than from a live session alone.
/// </param>
/// <param name="NewEmail">
/// The address being moved to, and where the confirming token is delivered. Nothing changes until
/// that token comes back.
/// </param>
public sealed record RequestEmailChangeCommand(string CurrentPassword, string NewEmail);
