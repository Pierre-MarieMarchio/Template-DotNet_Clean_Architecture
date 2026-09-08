using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Logout;

/// <summary>Idempotent, and never reveals whether the token existed.</summary>
public interface ILogoutUseCase : IUseCase<LogoutCommand, Result>;
