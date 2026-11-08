using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;

public sealed record PurgeExpiredRefreshTokensCommand : ICommand<Result<int>>;

public sealed class PurgeExpiredRefreshTokensCommandHandler(
    ISessionTokensService sessionTokens,
    TimeProvider timeProvider) : ICommandHandler<PurgeExpiredRefreshTokensCommand, Result<int>>
{
    public async ValueTask<Result<int>> Handle(PurgeExpiredRefreshTokensCommand command, CancellationToken cancellationToken) =>
        await sessionTokens.PurgeExpiredRefreshTokensAsync(timeProvider.GetUtcNow(), cancellationToken);
}
