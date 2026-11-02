using AppTemplate.Application.Auth.Features.AccessKeys.Model.Errors;
using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.CreateAccessKey;

public sealed class CreateAccessKeyUseCase(
    IAccessKeysService accessKeys,
    IAccountsService accounts,
    IReAuthenticationService reAuthentication,
    ICurrentAccessToken currentAccessToken,
    IDateTimeProvider dateTimeProvider,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<CreateAccessKeyCommand> validator) : ICreateAccessKeyUseCase
{
    public async Task<Result<CreateAccessKeyOutcome>> ExecuteAsync(
        CreateAccessKeyCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<CreateAccessKeyOutcome>();
        }

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId.To<CreateAccessKeyOutcome>();
        }

        if (request.ExpiresAt is { } expiresAt && expiresAt <= dateTimeProvider.UtcNow)
        {
            return Result.Failure<CreateAccessKeyOutcome>(AccessKeysErrors.ExpiryNotInTheFuture);
        }

        var userId = callerId.Value.Value;
        var proof = await ReAuthenticateAsync(userId, request.CurrentPassword, cancellationToken);

        if (proof.IsFailure)
        {
            return proof.To<CreateAccessKeyOutcome>();
        }

        if (await accessKeys.CountLiveAsync(userId, cancellationToken) >= AccessKeyPolicy.MaxActiveKeysPerAccount)
        {
            return Result.Failure<CreateAccessKeyOutcome>(AccessKeysErrors.TooManyAccessKeys);
        }

        var issued = await accessKeys.IssueAsync(
            userId,
            request.Name,
            request.Scopes,
            request.ExpiresAt,
            cancellationToken);

        securityEventLog.Record(SecurityEvent.AccessKeyCreated(userId));

        return Result.Success(new CreateAccessKeyOutcome(
            issued.Id,
            request.Name,
            issued.Prefix,
            request.Scopes,
            issued.CreatedAt,
            request.ExpiresAt,
            issued.Key));
    }

    private async Task<Result> ReAuthenticateAsync(
        Guid userId,
        string? currentPassword,
        CancellationToken cancellationToken)
    {
        var profile = await accounts.FindProfileAsync(userId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (profile.HasPassword)
        {
            if (currentPassword is null)
            {
                return Result.Failure(AccountsErrors.MissingCurrentPassword);
            }

            return await reAuthentication.VerifyPasswordAsync(userId, currentPassword, cancellationToken)
                ? Result.Success()
                : Result.Failure(AccountsErrors.IncorrectCurrentPassword);
        }

        return currentAccessToken.IssuedAt is { } issuedAt
            && dateTimeProvider.UtcNow - issuedAt <= DeleteMyAccountUseCase.RecentAuthenticationWindow
                ? Result.Success()
                : Result.Failure(AccountsErrors.ReAuthenticationRequired);
    }
}
