using AppTemplate.Api.Features.Maintenance.Contracts.Responses;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Api.Features.Maintenance.Contracts.Mapping;

internal static class MaintenanceResponseMapping
{
    public static Result<PurgeResponse> ToPurgeResponse(Result<int> result) =>
        result.Map(value => new PurgeResponse(value));
}
