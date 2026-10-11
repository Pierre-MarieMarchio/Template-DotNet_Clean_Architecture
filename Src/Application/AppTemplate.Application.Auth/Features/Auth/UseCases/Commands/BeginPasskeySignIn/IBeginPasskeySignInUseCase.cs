using AppTemplate.Application.Auth.Features.Auth.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.BeginPasskeySignIn;

/// <summary>Takes no input: no account is named, and the authenticator names one itself.</summary>
public interface IBeginPasskeySignInUseCase : IUseCase<Result<PasskeySignInChallenge>>;
