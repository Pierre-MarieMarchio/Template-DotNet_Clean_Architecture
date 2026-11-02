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

        var proof = await ReAuthenticateAsync(userId, profile, request, cancellationToken);

        if (proof.IsFailure)
        {
            return proof;
        }

        return await DeleteAsync(userId, cancellationToken);
    }

    private async Task<Result> ReAuthenticateAsync(
        Guid userId,
        UserProfile profile,
        DeleteMyAccountCommand request,
        CancellationToken cancellationToken)
    {
        if (!profile.HasPassword && !profile.TwoFactorEnabled)
        {
            return SignedInRecently() ? Result.Success() : Result.Failure(AccountsErrors.ReAuthenticationRequired);
        }

        if (profile.HasPassword)
        {
            var password = await VerifyPasswordAsync(userId, request.CurrentPassword, cancellationToken);

            if (password.IsFailure)
            {
                return password;
            }
        }

        return profile.TwoFactorEnabled
            ? await VerifyTwoFactorCodeAsync(userId, request.TwoFactorCode, cancellationToken)
            : Result.Success();
    }

    private async Task<Result> VerifyPasswordAsync(Guid userId, string? password, CancellationToken cancellationToken)
    {
        if (password is null)
        {
            return Result.Failure(AccountsErrors.MissingCurrentPassword);
        }

        return await reAuthentication.VerifyPasswordAsync(userId, password, cancellationToken)
            ? Result.Success()
            : Result.Failure(AccountsErrors.IncorrectCurrentPassword);
    }

    private async Task<Result> VerifyTwoFactorCodeAsync(Guid userId, string? code, CancellationToken cancellationToken)
    {
        if (code is null)
        {
            return Result.Failure(AccountsErrors.MissingTwoFactorCode);
        }

        return await reAuthentication.VerifyTwoFactorCodeAsync(userId, code, cancellationToken)
            ? Result.Success()
            : Result.Failure(AccountsErrors.InvalidTwoFactorConfirmationCode);
    }

    private bool SignedInRecently() =>
        currentAccessToken.IssuedAt is { } issuedAt
        && dateTimeProvider.UtcNow - issuedAt <= RecentAuthenticationWindow;

    private async Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
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
