using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Login;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.VerifyTwoFactor;

/// <summary>
/// The second step of a two-step login. Answers with <see cref="LoginOutcome"/> — the same
/// hierarchy <c>ILoginUseCase</c> answers with — rather than a response of its own, because the only
/// outcome this step ever produces is <see cref="LoginOutcome.Authenticated"/>: reusing the type
/// keeps one HTTP contract for "a login just finished" instead of two that a client would have to
/// tell apart for no reason.
/// </summary>
public interface IVerifyTwoFactorUseCase : IUseCase<VerifyTwoFactorCommand, Result<LoginOutcome>>;
