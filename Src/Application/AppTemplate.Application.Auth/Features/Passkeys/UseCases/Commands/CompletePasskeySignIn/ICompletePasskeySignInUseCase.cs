using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;

public interface ICompletePasskeySignInUseCase : IUseCase<CompletePasskeySignInCommand, Result<LoginOutcome>>;
