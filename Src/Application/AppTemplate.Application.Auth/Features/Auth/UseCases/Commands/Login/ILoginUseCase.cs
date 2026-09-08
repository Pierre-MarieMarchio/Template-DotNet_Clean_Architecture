using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Login;

/// <summary>
/// Anonymous. Ends in one of two places — a token pair, or a challenge to redeem — and every
/// refusal reaches the caller as the same error.
/// </summary>
public interface ILoginUseCase : IUseCase<LoginCommand, Result<LoginOutcome>>;
