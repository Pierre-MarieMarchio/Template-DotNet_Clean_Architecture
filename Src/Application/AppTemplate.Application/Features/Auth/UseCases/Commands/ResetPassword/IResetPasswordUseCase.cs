using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ResetPassword;

public interface IResetPasswordUseCase : IUseCase<ResetPasswordCommand, Result>;
