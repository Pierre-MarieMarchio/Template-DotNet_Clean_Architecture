namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// Travels in a request body, never a query string: the single-use token must not land in server
/// access logs, browser history or a <c>Referer</c> header.
/// </summary>
public sealed record ConfirmEmailRequest(string Email, string Token);
