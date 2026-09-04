using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Login;

public interface ILoginUseCase : IUseCase<LoginCommand, Result<LoginOutcome>>;
