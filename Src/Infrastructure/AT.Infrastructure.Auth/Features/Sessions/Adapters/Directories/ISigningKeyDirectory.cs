using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Configuration.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Directories;

internal interface ISigningKeyDirectory
{
    Task<IReadOnlyCollection<SecurityKey>> GetAsync(
        ExternalIdentityProviderOptions provider,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SecurityKey>> RefreshAsync(
        ExternalIdentityProviderOptions provider,
        CancellationToken cancellationToken = default);
}
