using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.SetUpTwoFactor;

public interface ISetUpTwoFactorUseCase : IUseCase<Result<SetUpTwoFactorOutcome>>;
