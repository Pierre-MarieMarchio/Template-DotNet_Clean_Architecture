using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.LockAccount;

/// <summary>Administrator-only: see <c>AuthorizationPolicies.Administrator</c> on the endpoint that exposes this.</summary>
public interface ILockAccountUseCase : IUseCase<LockAccountCommand, Result>;
