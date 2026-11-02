namespace AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;

public sealed record SignInWithExternalProviderCommand(string Provider, string IdToken);
