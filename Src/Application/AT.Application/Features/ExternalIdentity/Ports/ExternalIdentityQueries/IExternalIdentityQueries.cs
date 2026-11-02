using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;

public interface IExternalIdentityQueries
{
    Task<ExternalIdentityProfile?> FindProfileAsync(UserId userId, CancellationToken cancellationToken = default);
}
