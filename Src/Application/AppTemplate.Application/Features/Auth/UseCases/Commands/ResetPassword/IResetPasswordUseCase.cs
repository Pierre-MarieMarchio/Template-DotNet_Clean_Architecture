using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ResetPassword;

public interface IResetPasswordUseCase : IUseCase<ResetPasswordCommand, Result>;
