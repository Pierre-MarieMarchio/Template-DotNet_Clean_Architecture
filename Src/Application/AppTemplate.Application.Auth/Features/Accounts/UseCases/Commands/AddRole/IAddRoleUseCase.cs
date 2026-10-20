using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.AddRole;

public interface IAddRoleUseCase : IUseCase<AddRoleCommand, Result>;
