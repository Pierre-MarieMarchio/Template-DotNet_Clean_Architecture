using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.DisableAccountTwoFactor;

public interface IDisableAccountTwoFactorUseCase : IUseCase<DisableAccountTwoFactorCommand, Result>;
