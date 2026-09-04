using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.UnlockAccount;

/// <summary>Administrator-only: see <c>Policies.Administrator</c> on the endpoint that exposes this.</summary>
public interface IUnlockAccountUseCase : IUseCase<UnlockAccountCommand, Result>;
