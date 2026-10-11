using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.ExternalIdentity.Errors;

/// <summary>
/// The failures this feature is allowed to disclose. There is only one: the mapping is the whole of
/// an account's local presence, and an administrative action naming an id no mapping carries is told
/// so — the caller is an administrator acting on an id it already has, not an anonymous one probing.
/// </summary>
public static class ExternalIdentityErrors
{
    /// <summary>No mapping names this owner: the account is already deleted, or never existed.</summary>
    public static Error MappingNotFound { get; } = Error.NotFound(
        "externalIdentity.mapping.notFound",
        "No account is known under this id.");
}
