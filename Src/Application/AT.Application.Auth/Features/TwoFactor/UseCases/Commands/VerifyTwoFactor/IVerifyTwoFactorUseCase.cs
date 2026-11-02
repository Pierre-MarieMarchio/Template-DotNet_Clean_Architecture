using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.VerifyTwoFactor;

public interface IVerifyTwoFactorUseCase : IUseCase<VerifyTwoFactorCommand, Result<LoginOutcome>>;
