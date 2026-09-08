using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.SetUpTwoFactor;

/// <summary>Whole input is ambient: the caller's own id, taken from the request's principal.</summary>
public interface ISetUpTwoFactorUseCase : IUseCase<Result<SetUpTwoFactorOutcome>>;
