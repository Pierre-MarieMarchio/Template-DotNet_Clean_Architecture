using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RequestEmailChange;

/// <summary>
/// Authenticated. The caller proves they still hold the current password before the account's
/// address moves — a stolen session alone must not be able to redirect it to an address the
/// attacker controls.
/// </summary>
public interface IRequestEmailChangeUseCase : IUseCase<RequestEmailChangeCommand, Result>;
