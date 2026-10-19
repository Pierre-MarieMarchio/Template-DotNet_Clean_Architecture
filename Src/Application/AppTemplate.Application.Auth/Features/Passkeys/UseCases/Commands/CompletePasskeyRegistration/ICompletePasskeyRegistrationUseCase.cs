using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;

/// <summary>
/// Stores the credential a client created, once its ceremony verifies against the state the
/// options step issued — the second and completing half of registering a passkey.
/// </summary>
public interface ICompletePasskeyRegistrationUseCase
    : IUseCase<CompletePasskeyRegistrationCommand, Result<PasskeyCredential>>;
