using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;

public interface IRefreshAccessTokenUseCase : IUseCase<RefreshAccessTokenCommand, Result<RefreshAccessTokenResponse>>;
