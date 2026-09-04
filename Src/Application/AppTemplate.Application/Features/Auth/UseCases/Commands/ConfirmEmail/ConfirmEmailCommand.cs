namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;

/// <summary>
/// Sent in a request body rather than a query string, to keep the single-use token out of access
/// logs, browser history and the <c>Referer</c> header.
/// </summary>
public sealed record ConfirmEmailCommand(string Email, string Token);
