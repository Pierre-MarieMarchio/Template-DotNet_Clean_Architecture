using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmail;

public interface IConfirmEmailUseCase : IUseCase<ConfirmEmailCommand, Result>;
