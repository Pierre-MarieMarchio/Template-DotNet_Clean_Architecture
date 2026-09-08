namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RequestPasswordReset;

/// <param name="Email">
/// The address a reset link is asked for. Never confirmed back to the caller as known or unknown.
/// </param>
public sealed record RequestPasswordResetCommand(string Email);
