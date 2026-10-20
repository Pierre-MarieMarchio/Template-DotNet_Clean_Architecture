namespace AppTemplate.Application.Auth.Features.Accounts.Ports.ReAuthentication;

public interface IReAuthenticationService
{
    Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    Task<bool> VerifyTwoFactorCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default);
}
