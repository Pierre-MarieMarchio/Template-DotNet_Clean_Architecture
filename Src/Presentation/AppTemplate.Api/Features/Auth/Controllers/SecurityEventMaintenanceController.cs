using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Maintenance.Contracts.Responses;
using AppTemplate.Api.Features.Maintenance.Mapping;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.PurgeExpiredSecurityEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>
/// The security-event trail's retention purge, under the maintenance prefix and tag so the API
/// surface is the same whichever feature answers it. It lives with the rest of authentication so
/// that a host without local accounts loses it with the feature.
/// </summary>
/// <remarks>
/// Also run by the API's maintenance job; this endpoint is what a scheduler outside the process
/// calls. What "expired" means is the <c>SecurityEventLog</c> section's retention, not a
/// parameter: a caller has no business choosing how long evidence is kept.
/// </remarks>
[Route("api/v{version:apiVersion}/maintenance")]
[Asp.Versioning.ApiVersion("1.0")]
[Tags("Maintenance")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
public sealed class SecurityEventMaintenanceController(
    IPurgeExpiredSecurityEventsUseCase purgeExpiredSecurityEvents) : ApiControllerBase
{
    /// <summary>
    /// Deletes every security event older than the retention period — for every account, a
    /// deleted one included, whose rows are removed here and nowhere else.
    /// </summary>
    [HttpDelete("security-events/expired")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PurgeResponse))]
    public async Task<ActionResult<PurgeResponse>> PurgeExpiredSecurityEvents(CancellationToken cancellationToken) =>
        OkOrProblem(
            MaintenanceResponseMapping.ToPurgeResponse(await purgeExpiredSecurityEvents.ExecuteAsync(cancellationToken)));
}
