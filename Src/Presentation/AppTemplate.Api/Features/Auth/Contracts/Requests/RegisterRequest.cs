namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

public sealed record RegisterRequest(string UserName, string Email, string Password);
