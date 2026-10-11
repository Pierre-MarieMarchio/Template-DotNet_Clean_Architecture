using AppTemplate.Application.Auth.Features.Auth.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.BeginPasskeyRegistration;

/// <summary>
/// Whole input is ambient: the caller's own id, taken from the request's principal, names the
/// account the credential is registered on.
/// </summary>
public interface IBeginPasskeyRegistrationUseCase : IUseCase<Result<PasskeyRegistrationChallenge>>;
