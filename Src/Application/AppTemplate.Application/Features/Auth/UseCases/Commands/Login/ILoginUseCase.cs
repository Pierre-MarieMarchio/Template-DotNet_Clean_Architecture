using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Login;

public interface ILoginUseCase : IUseCase<LoginCommand, Result<LoginOutcome>>;
