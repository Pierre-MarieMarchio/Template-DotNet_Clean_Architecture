namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmailChange;

/// <summary>
/// Authenticated: the token is validated against the caller's own account rather than looked up by
/// address, because the new address is not confirmed — and so not yet on file — until this call
/// succeeds. Sent in a request body rather than a query string, for the reason
/// <c>ConfirmEmailCommand</c> gives.
/// </summary>
public sealed record ConfirmEmailChangeCommand(string NewEmail, string Token);
