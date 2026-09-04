using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ResetPassword;

public interface IResetPasswordUseCase : IUseCase<ResetPasswordCommand, Result>;
