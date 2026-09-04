using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Directories;

/// <summary>
/// The public keys a provider signs its <c>id_token</c>s with, cached.
/// <para>
/// Internal, and not an application port: a use case has no business naming a signing key, and a
/// contract outside this module able to hand back "the keys a token is checked against" would be a
/// seam through which the wrong keys could be supplied.
/// </para>
/// <para>
/// The two operations are separate because they answer different questions.
/// <see cref="GetAsync"/> is what every verification asks and is normally served from memory;
/// <see cref="RefreshAsync"/> is asked only when a token named a key the cached set does not hold,
/// which is what a rotation looks like from here.
/// </para>
/// </summary>
internal interface ISigningKeyDirectory
{
    /// <summary>
    /// The keys currently believed good for the provider. Empty when the provider has never been
    /// reachable — which refuses the token, rather than accepting it unverified.
    /// </summary>
    Task<IReadOnlyCollection<SecurityKey>> GetAsync(
        ExternalIdentityProviderOptions provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches again because a token named a key that is not in the cached set, subject to a floor
    /// on how often that may happen: without one, tokens carrying a <c>kid</c> nobody ever published
    /// would turn into one outbound request each.
    /// </summary>
    Task<IReadOnlyCollection<SecurityKey>> RefreshAsync(
        ExternalIdentityProviderOptions provider,
        CancellationToken cancellationToken = default);
}
