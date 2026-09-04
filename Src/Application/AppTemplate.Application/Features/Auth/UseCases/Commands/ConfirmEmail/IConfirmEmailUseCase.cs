using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;

public interface IConfirmEmailUseCase : IUseCase<ConfirmEmailCommand, Result>;
