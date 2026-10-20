using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResendConfirmationEmail;

public interface IResendConfirmationEmailUseCase : IUseCase<ResendConfirmationEmailCommand, Result>;
