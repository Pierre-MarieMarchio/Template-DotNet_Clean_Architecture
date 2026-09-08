using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmail;

/// <summary>
/// Anonymous: the token is the whole authorisation, since the holder cannot sign in until this
/// call has succeeded.
/// </summary>
public interface IConfirmEmailUseCase : IUseCase<ConfirmEmailCommand, Result>;
