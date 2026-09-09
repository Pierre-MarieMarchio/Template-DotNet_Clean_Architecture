using AppTemplate.Api.Features.Maintenance.Contracts.Responses;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Api.Features.Maintenance.Mapping;

/// <summary>
/// Projects the feature's use case outcomes onto its wire contracts.
/// </summary>
internal static class MaintenanceResponseMapping
{
    public static Result<PurgeResponse> ToPurgeResponse(Result<int> result) =>
        result.Map(value => new PurgeResponse(value));
}
