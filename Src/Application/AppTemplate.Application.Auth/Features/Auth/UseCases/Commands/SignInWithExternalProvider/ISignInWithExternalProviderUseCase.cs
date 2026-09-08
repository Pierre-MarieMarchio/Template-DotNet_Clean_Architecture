using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.SignInWithExternalProvider;

/// <summary>
/// Anonymous, and ends in the same two shapes a password sign-in does: a provider stands in for a
/// password, never for a second factor.
/// </summary>
public interface ISignInWithExternalProviderUseCase
    : IUseCase<SignInWithExternalProviderCommand, Result<SignInWithExternalProviderOutcome>>;
