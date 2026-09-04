namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// Travels in a request body, never a query string, for the reason
/// <see cref="ConfirmEmailRequest"/> gives.
/// </summary>
public sealed record ConfirmEmailChangeRequest(string NewEmail, string Token);
