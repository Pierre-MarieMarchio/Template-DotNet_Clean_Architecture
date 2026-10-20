namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.Register;

/// <summary>An anonymous request for an account.</summary>
/// <param name="Email">The account's only identifier.</param>
/// <param name="Password">The password the account signs in with.</param>
public sealed record RegisterCommand(string Email, string Password);
