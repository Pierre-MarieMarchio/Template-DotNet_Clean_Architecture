using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Register;

public interface IRegisterUseCase : IUseCase<RegisterCommand, Result<RegisterOutcome>>;
