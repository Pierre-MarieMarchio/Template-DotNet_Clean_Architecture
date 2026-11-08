namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// The address and its single-use token, sent in the body so the token stays out of logs and URLs.
/// </summary>
public sealed record ConfirmEmailRequest(string Email, string Token);
