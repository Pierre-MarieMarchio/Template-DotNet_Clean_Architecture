using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.RevokeAccessKey;

public interface IRevokeAccessKeyUseCase : IUseCase<RevokeAccessKeyCommand, Result>;
