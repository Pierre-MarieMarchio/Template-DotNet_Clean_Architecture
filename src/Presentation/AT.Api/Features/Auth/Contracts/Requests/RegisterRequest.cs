namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>The address and password an account is requested with.</summary>
public sealed record RegisterRequest(string Email, string Password);
