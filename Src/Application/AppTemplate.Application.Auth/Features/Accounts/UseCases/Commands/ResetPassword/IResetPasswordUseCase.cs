using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResetPassword;

public interface IResetPasswordUseCase : IUseCase<ResetPasswordCommand, Result>;
