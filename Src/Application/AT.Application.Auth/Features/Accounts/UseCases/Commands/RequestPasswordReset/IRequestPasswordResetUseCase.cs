using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestPasswordReset;

public interface IRequestPasswordResetUseCase : IUseCase<RequestPasswordResetCommand, Result>;
