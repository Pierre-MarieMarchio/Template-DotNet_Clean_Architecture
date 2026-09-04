using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.SignInWithExternalProvider;

public interface ISignInWithExternalProviderUseCase
    : IUseCase<SignInWithExternalProviderCommand, Result<SignInWithExternalProviderOutcome>>;
