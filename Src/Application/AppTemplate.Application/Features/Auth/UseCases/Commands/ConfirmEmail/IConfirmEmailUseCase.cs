using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;

public interface IConfirmEmailUseCase : IUseCase<ConfirmEmailCommand, Result>;
