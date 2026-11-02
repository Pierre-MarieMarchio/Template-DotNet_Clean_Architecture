using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;

public sealed class PurgeExpiredRefreshTokensUseCase(
    ISessionTokensService sessionTokens,
    IDateTimeProvider dateTimeProvider) : IPurgeExpiredRefreshTokensUseCase
{
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await sessionTokens.PurgeExpiredRefreshTokensAsync(dateTimeProvider.UtcNow, cancellationToken);
}
