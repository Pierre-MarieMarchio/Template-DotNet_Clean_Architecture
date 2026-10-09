using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;

/// <summary>
/// Reads of the mapping by owner, for work that has an owner id and no token, such as a background
/// pass that rings a reminder.
/// </summary>
public interface IExternalIdentityQueries
{
    /// <returns>
    /// The profile of the most recently seen mapping of <paramref name="userId"/> (an owner carried
    /// over from a previous provider can have several), or <c>null</c> when no mapping names it.
    /// </returns>
    Task<ExternalIdentityProfile?> FindProfileAsync(UserId userId, CancellationToken cancellationToken = default);
}
