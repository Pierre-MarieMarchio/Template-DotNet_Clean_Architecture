using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

/// <summary>Authenticated. Proves the caller can produce a code before two-factor sign-in turns on.</summary>
public interface IConfirmTwoFactorSetupUseCase
    : IUseCase<ConfirmTwoFactorSetupCommand, Result<ConfirmTwoFactorSetupOutcome>>;
