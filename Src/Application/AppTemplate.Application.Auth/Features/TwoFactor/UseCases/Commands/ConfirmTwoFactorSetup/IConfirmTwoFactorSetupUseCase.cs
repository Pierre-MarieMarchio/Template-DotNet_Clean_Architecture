using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.ConfirmTwoFactorSetup;

public interface IConfirmTwoFactorSetupUseCase
    : IUseCase<ConfirmTwoFactorSetupCommand, Result<ConfirmTwoFactorSetupOutcome>>;
