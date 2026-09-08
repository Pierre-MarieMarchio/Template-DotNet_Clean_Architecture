using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RefreshAccessToken;

/// <summary>
/// Anonymous: the presented grant is the whole authorisation, since the access token it renews has
/// usually expired by the time this is called.
/// </summary>
public interface IRefreshAccessTokenUseCase : IUseCase<RefreshAccessTokenCommand, Result<RefreshAccessTokenOutcome>>;
