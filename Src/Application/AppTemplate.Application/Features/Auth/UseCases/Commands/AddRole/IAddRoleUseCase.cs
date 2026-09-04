using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.AddRole;

/// <summary>Administrator-only: see <c>Policies.Administrator</c> on the endpoint that exposes this.</summary>
public interface IAddRoleUseCase : IUseCase<AddRoleCommand, Result>;
