using AppTemplate.Application.Auth.Features.AccessKeys.Model.Errors;
using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.AuthenticateAccessKey;

public sealed record AuthenticateAccessKeyCommand(string Key) : ICommand<Result<AccessKeyCaller>>;

public sealed class AuthenticateAccessKeyCommandHandler(
    IAccessKeysService accessKeys,
    IAccountsService accounts,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<AuthenticateAccessKeyCommand, Result<AccessKeyCaller>>
{
    public async ValueTask<Result<AccessKeyCaller>> Handle(
        AuthenticateAccessKeyCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!AccessKeyPolicy.IsWellFormed(request.Key))
        {
            return Result.Failure<AccessKeyCaller>(AccessKeysErrors.InvalidKey);
        }

        var now = dateTimeProvider.UtcNow;
        var grant = await accessKeys.FindAsync(request.Key, cancellationToken);

        if (grant is null
            || !grant.IsLiveAt(now)
            || !await accounts.CanSignInAsync(grant.UserId, cancellationToken))
        {
            return Result.Failure<AccessKeyCaller>(AccessKeysErrors.InvalidKey);
        }

        if (grant.LastUsedAt is not { } lastUsedAt || now - lastUsedAt >= AccessKeyPolicy.LastUseResolution)
        {
            await accessKeys.RecordUseAsync(grant.AccessKeyId, now, cancellationToken);
        }

        return Result.Success(new AccessKeyCaller(grant.UserId, grant.AccessKeyId, grant.Scopes));
    }
}
