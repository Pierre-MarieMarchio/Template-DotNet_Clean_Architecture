using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeySignIn;

public sealed class BeginPasskeySignInUseCase(IPasskeyService passkeys) : IBeginPasskeySignInUseCase
{
    public async Task<Result<PasskeySignInChallenge>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        Result.Success(await passkeys.BeginSignInAsync(cancellationToken));
}
