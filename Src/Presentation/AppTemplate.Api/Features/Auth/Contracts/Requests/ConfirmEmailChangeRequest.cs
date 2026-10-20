namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// The new address and the token mailed to it, sent in the body as <see
/// cref="ConfirmEmailRequest"/> is.
/// </summary>
public sealed record ConfirmEmailChangeRequest(string NewEmail, string Token);
