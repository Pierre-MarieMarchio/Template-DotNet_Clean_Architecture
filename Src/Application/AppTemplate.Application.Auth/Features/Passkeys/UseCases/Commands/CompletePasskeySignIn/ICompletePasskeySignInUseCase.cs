using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;

/// <summary>
/// The second step of a passkey sign-in. Answers with <see cref="LoginOutcome"/> — the same
/// hierarchy <c>ILoginUseCase</c> answers with — rather than a response of its own, for the same
/// reason <c>IVerifyTwoFactorUseCase</c> does: one HTTP contract for "a login just finished",
/// which here is always the <see cref="LoginOutcome.Authenticated"/> branch.
/// </summary>
public interface ICompletePasskeySignInUseCase : IUseCase<CompletePasskeySignInCommand, Result<LoginOutcome>>;
