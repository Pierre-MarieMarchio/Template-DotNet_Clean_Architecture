using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;

public interface ISignInWithExternalProviderUseCase
    : IUseCase<SignInWithExternalProviderCommand, Result<SignInWithExternalProviderOutcome>>;
