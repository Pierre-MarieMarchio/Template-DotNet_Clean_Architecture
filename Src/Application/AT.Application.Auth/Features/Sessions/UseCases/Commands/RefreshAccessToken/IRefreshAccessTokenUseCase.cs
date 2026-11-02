using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RefreshAccessToken;

public interface IRefreshAccessTokenUseCase : IUseCase<RefreshAccessTokenCommand, Result<RefreshAccessTokenOutcome>>;
