using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeyRegistration;

public interface IBeginPasskeyRegistrationUseCase : IUseCase<Result<PasskeyRegistrationChallenge>>;
