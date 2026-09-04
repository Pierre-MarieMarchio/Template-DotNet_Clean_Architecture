using AppTemplate.Api.Features.Maintenance.Contracts.Responses;
using AppTemplate.Application.Common;

namespace AppTemplate.Api.Features.Maintenance.Mapping;

/// <summary>
/// Projects the feature's use case outcomes onto its wire contracts, by hand.
/// </summary>
/// <remarks>
/// One field is still worth a mapper of its own: every feature here puts its wire shapes behind one,
/// so the place to add a field to a response is the same place in every folder.
/// </remarks>
internal static class MaintenanceMapping
{
    public static Result<PurgeResponse> ToPurgeResponse(Result<int> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // Value throws on a failure, so the guard is a statement of its own rather than a subpattern.
        if (result.IsFailure)
        {
            return result.To<PurgeResponse>();
        }

        return new PurgeResponse(result.Value);
    }
}
