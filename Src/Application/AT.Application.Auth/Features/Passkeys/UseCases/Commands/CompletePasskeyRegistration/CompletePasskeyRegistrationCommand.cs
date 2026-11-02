namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;

public sealed record CompletePasskeyRegistrationCommand(
    string Credential,
    string RegistrationState,
    string? Name,
    string? CurrentPassword);
