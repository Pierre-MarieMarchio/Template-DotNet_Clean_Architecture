namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Register;

/// <param name="UserName">Bounded by <c>RegisterCommandValidator.MaximumUserNameLength</c>.</param>
/// <param name="Email">
/// Where the confirmation link goes, and the identifier sign-in later runs against. Nothing about
/// the account works until someone proves they can read mail here.
/// </param>
/// <param name="Password">Held to <c>PasswordPolicy</c>'s bounds; character classes are the store's.</param>
public sealed record RegisterCommand(string UserName, string Email, string Password);
