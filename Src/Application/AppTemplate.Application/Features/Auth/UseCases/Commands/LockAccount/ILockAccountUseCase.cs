using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.LockAccount;

/// <summary>Administrator-only: see <c>AuthorizationPolicies.Administrator</c> on the endpoint that exposes this.</summary>
public interface ILockAccountUseCase : IUseCase<LockAccountCommand, Result>;
