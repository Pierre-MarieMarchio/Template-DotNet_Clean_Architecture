using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DisableTwoFactor;

/// <summary>Authenticated. The caller proves they still hold the current password before the second factor is stripped.</summary>
public interface IDisableTwoFactorUseCase : IUseCase<DisableTwoFactorCommand, Result>;
