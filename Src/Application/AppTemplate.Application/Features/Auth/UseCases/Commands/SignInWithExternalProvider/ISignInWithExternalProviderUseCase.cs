using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.SignInWithExternalProvider;

public interface ISignInWithExternalProviderUseCase
    : IUseCase<SignInWithExternalProviderCommand, Result<SignInWithExternalProviderOutcome>>;
