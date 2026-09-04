using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DisableTwoFactor;

/// <summary>Authenticated. The caller proves they still hold the current password before the second factor is stripped.</summary>
public interface IDisableTwoFactorUseCase : IUseCase<DisableTwoFactorCommand, Result>;
