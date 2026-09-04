using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DisableAccountTwoFactor;

/// <summary>
/// Administrative. Strips a second factor nobody can prove possession of any more, without going as
/// far as <c>IDeleteAccountUseCase</c>.
/// </summary>
public interface IDisableAccountTwoFactorUseCase : IUseCase<DisableAccountTwoFactorCommand, Result>;
