using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

/// <summary>Authenticated. Proves the caller can produce a code before two-factor sign-in turns on.</summary>
public interface IConfirmTwoFactorSetupUseCase
    : IUseCase<ConfirmTwoFactorSetupCommand, Result<ConfirmTwoFactorSetupOutcome>>;
