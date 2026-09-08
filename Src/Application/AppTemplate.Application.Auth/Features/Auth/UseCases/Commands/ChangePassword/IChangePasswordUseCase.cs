using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ChangePassword;

/// <summary>Authenticated. The caller proves they still hold the current password before it is replaced.</summary>
public interface IChangePasswordUseCase : IUseCase<ChangePasswordCommand, Result>;
