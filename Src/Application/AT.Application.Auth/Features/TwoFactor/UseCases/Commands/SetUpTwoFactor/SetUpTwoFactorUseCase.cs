using AppTemplate.Application.Auth.Features.TwoFactor.Model.Errors;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.SetUpTwoFactor;

public sealed class SetUpTwoFactorUseCase(
    ITwoFactorService twoFactor,
    ICurrentUser currentUser) : ISetUpTwoFactorUseCase
{
    public async Task<Result<SetUpTwoFactorOutcome>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<SetUpTwoFactorOutcome>();
        }

        var started = await twoFactor.BeginSetupAsync(userId.Value.Value, cancellationToken);

        if (started.Status is not TwoFactorSetupStatus.Started)
        {
            return Result.Failure<SetUpTwoFactorOutcome>(ToError(started.Status));
        }

        return Result.Success(new SetUpTwoFactorOutcome(started.SharedKey!, started.AuthenticatorUri!));
    }

    private static Error ToError(TwoFactorSetupStatus status) => status switch
    {
        TwoFactorSetupStatus.AlreadyEnabled => TwoFactorErrors.TwoFactorAlreadyEnabled,

        TwoFactorSetupStatus.NoSuchAccount => CommonErrors.NotAuthenticated,
        _ => TwoFactorErrors.TwoFactorSetupRejected,
    };
}
