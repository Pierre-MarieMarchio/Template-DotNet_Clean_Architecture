using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Queries.ListPasskeys;

/// <summary>
/// The passkeys registered on the caller's own account — the list a client shows next to "sign in
/// with a passkey", and the ids its removal routes take.
/// </summary>
public sealed class ListPasskeysUseCase(
    IPasskeyService passkeys,
    ICurrentUser currentUser) : IListPasskeysUseCase
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PasskeyCredential>>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId.To<IReadOnlyList<PasskeyCredential>>();
        }

        return Result.Success(
            await passkeys.ListAsync(callerId.Value.Value, cancellationToken));
    }
}
