using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Domain.Core.Common.Primitives;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;

public sealed class DeleteMyAccountUseCase(
    IAccountsService accounts,
    IReAuthenticationService reAuthentication,
    ICurrentAccessToken currentAccessToken,
    ISecurityEventLog securityEventLog,
    IDomainEventPublisher domainEvents,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<DeleteMyAccountCommand> validator) : IDeleteMyAccountUseCase
{
    public static readonly TimeSpan RecentAuthenticationWindow = TimeSpan.FromMinutes(5);

    public async Task<Result> ExecuteAsync(DeleteMyAccountCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId;
        }

        var userId = callerId.Value.Value;

        var profile = await accounts.FindProfileAsync(userId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (profile.HasPassword)
        {
            if (request.CurrentPassword is null)
            {
                return Result.Failure(AccountsErrors.MissingCurrentPassword);
            }

            if (!await reAuthentication.VerifyPasswordAsync(userId, request.CurrentPassword, cancellationToken))
            {
                return Result.Failure(AccountsErrors.IncorrectCurrentPassword);
            }
        }

        if (profile.TwoFactorEnabled)
        {
            if (request.TwoFactorCode is null)
            {
                return Result.Failure(AccountsErrors.MissingTwoFactorCode);
            }

            if (!await reAuthentication.VerifyTwoFactorCodeAsync(userId, request.TwoFactorCode, cancellationToken))
            {
                return Result.Failure(AccountsErrors.InvalidTwoFactorConfirmationCode);
            }
        }

        if (!profile.HasPassword && !profile.TwoFactorEnabled)
        {
            if (currentAccessToken.IssuedAt is not { } issuedAt
                || dateTimeProvider.UtcNow - issuedAt > RecentAuthenticationWindow)
            {
                return Result.Failure(AccountsErrors.ReAuthenticationRequired);
            }
        }

        var deletion = await accounts.DeleteAsync(userId, cancellationToken);

        if (deletion.Status is AccountDeletionStatus.NoSuchAccount)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (deletion.Status is not AccountDeletionStatus.Deleted)
        {
            return Result.Failure(AccountsErrors.AccountDeletionRejected);
        }

        securityEventLog.Record(SecurityEvent.AccountSelfDeleted(userId));

        await domainEvents.PublishAsync(
            new UserAccountDeleted(UserId.Create(userId), dateTimeProvider.UtcNow),
            cancellationToken);

        return Result.Success();
    }
}
