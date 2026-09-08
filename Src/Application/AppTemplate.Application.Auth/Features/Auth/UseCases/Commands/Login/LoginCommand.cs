namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Login;

/// <param name="Email">The address the caller claims. Whether an account holds it is never said.</param>
/// <param name="Password">
/// Checked presence only before the store sees it: an existing account may predate the current
/// policy, so a length rule here would refuse a password that still works.
/// </param>
public sealed record LoginCommand(string Email, string Password);
