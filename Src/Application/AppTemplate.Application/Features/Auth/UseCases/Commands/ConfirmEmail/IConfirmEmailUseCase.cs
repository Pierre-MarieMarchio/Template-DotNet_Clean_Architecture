using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;

public interface IConfirmEmailUseCase : IUseCase<ConfirmEmailCommand, Result>;
