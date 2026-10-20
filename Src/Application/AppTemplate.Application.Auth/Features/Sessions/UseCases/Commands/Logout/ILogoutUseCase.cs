using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Logout;

public interface ILogoutUseCase : IUseCase<LogoutCommand, Result>;
