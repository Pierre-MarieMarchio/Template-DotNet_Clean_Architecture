using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.AuthenticateAccessKey;

public interface IAuthenticateAccessKeyUseCase : IUseCase<AuthenticateAccessKeyCommand, Result<AccessKeyCaller>>;
