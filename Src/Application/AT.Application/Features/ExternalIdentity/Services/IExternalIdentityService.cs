using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Services;

public interface IExternalIdentityService
{
    Task<UserId> ResolveAsync(ExternalCallerDto caller, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListAssignedRolesAsync(UserId userId, CancellationToken cancellationToken = default);

    Task<ExternalIdentityProfile?> FindProfileAsync(UserId userId, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(UserId userId, CancellationToken cancellationToken = default);
}
