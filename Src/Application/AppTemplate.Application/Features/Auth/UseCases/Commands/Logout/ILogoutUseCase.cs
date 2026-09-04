using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Logout;

/// <summary>Idempotent, and never reveals whether the token existed.</summary>
public interface ILogoutUseCase : IUseCase<LogoutCommand, Result>;
