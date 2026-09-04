using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Register;

public interface IRegisterUseCase : IUseCase<RegisterCommand, Result<RegisterOutcome>>;
