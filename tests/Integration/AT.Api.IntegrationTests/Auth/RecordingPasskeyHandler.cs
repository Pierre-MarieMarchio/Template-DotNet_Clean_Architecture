using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.IntegrationTests.Auth;

internal sealed class RecordingPasskeyHandler(
    UserManager<AppUser> userManager,
    IOptions<IdentityPasskeyOptions> options,
    PasskeyCeremonyFailures failures) : IPasskeyHandler<AppUser>
{
    private readonly PasskeyHandler<AppUser> _identity = new(userManager, options);

    public Task<PasskeyCreationOptionsResult> MakeCreationOptionsAsync(
        PasskeyUserEntity userEntity,
        HttpContext httpContext) =>
        _identity.MakeCreationOptionsAsync(userEntity, httpContext);

    public Task<PasskeyRequestOptionsResult> MakeRequestOptionsAsync(AppUser? user, HttpContext httpContext) =>
        _identity.MakeRequestOptionsAsync(user, httpContext);

    public async Task<PasskeyAttestationResult> PerformAttestationAsync(PasskeyAttestationContext context)
    {
        var result = await _identity.PerformAttestationAsync(context);

        if (result.Failure is { } failure)
        {
            failures.Record(failure.Message);
        }

        return result;
    }

    public async Task<PasskeyAssertionResult<AppUser>> PerformAssertionAsync(PasskeyAssertionContext context)
    {
        var result = await _identity.PerformAssertionAsync(context);

        if (result.Failure is { } failure)
        {
            failures.Record(failure.Message);
        }

        return result;
    }
}
