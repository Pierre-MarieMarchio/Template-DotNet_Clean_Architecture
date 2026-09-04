using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Login;

public interface ILoginUseCase : IUseCase<LoginCommand, Result<LoginOutcome>>;
