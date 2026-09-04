using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.UnlockAccount;

/// <summary>Administrator-only: see <c>AuthorizationPolicies.Administrator</c> on the endpoint that exposes this.</summary>
public interface IUnlockAccountUseCase : IUseCase<UnlockAccountCommand, Result>;
