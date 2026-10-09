using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Maintenance.Contracts.Responses;
using AppTemplate.Api.Features.Maintenance.Mapping;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.PurgeExpiredRefreshTokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>
/// Authentication's administrative purge, under the maintenance prefix and tag so the API surface is
/// the same whichever feature answers it. It lives here so that a host without local accounts loses
/// it with the rest of the feature.
/// </summary>
/// <remarks>
/// Also run by the API's maintenance job; this endpoint is what a scheduler outside the process
/// calls.
/// </remarks>
[Route("api/v{version:apiVersion}/maintenance")]
[Asp.Versioning.ApiVersion("1.0")]
[Tags("Maintenance")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
public sealed class RefreshTokenMaintenanceController(
    IPurgeExpiredRefreshTokensUseCase purgeExpiredRefreshTokens) : ApiControllerBase
{
    /// <summary>
    /// Deletes every refresh-token grant whose retention window has passed. Nothing a caller does
    /// triggers this: the table only grows, one row per rotation, until an operator prunes it.
    /// </summary>
    [HttpDelete("refresh-tokens/expired")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PurgeResponse))]
    public async Task<ActionResult<PurgeResponse>> PurgeExpiredRefreshTokens(CancellationToken cancellationToken) =>
        OkOrProblem(
            MaintenanceResponseMapping.ToPurgeResponse(await purgeExpiredRefreshTokens.ExecuteAsync(cancellationToken)));
}
