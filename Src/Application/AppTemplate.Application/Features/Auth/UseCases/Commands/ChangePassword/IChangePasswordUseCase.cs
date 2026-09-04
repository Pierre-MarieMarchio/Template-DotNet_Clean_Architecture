using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ChangePassword;

/// <summary>Authenticated. The caller proves they still hold the current password before it is replaced.</summary>
public interface IChangePasswordUseCase : IUseCase<ChangePasswordCommand, Result>;
