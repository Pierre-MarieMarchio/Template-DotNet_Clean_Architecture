using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResetPassword;

/// <summary>
/// Anonymous: the mailed token is the whole authorisation, since a caller who has forgotten the
/// password has nothing else to present.
/// </summary>
public interface IResetPasswordUseCase : IUseCase<ResetPasswordCommand, Result>;
