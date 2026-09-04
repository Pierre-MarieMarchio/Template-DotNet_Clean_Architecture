using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;

public interface IRefreshAccessTokenUseCase : IUseCase<RefreshAccessTokenCommand, Result<RefreshAccessTokenOutcome>>;
