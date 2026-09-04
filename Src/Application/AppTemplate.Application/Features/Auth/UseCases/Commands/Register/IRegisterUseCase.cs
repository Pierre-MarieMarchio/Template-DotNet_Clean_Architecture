using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Register;

public interface IRegisterUseCase : IUseCase<RegisterCommand, Result<RegisterResponse>>;
