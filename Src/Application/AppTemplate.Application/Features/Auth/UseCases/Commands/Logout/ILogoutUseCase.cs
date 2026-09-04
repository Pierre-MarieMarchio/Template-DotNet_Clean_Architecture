using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Logout;

/// <summary>Idempotent, and never reveals whether the token existed.</summary>
public interface ILogoutUseCase : IUseCase<LogoutCommand, Result>;
