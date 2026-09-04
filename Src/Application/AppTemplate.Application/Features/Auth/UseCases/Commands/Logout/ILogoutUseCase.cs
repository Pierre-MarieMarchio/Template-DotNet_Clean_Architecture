using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Logout;

/// <summary>Idempotent, and never reveals whether the token existed.</summary>
public interface ILogoutUseCase : IUseCase<LogoutCommand, Result>;
