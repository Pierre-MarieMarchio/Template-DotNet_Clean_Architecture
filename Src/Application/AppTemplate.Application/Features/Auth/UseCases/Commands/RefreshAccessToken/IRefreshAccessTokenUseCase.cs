using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;

public interface IRefreshAccessTokenUseCase : IUseCase<RefreshAccessTokenCommand, Result<RefreshAccessTokenOutcome>>;
