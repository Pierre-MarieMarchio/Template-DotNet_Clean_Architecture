using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Maintenance.Contracts.Responses;
using AppTemplate.Api.Features.Maintenance.Mapping;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Maintenance.Controllers;

/// <summary>
/// Administrative operations with no user-facing counterpart.
/// </summary>
/// <remarks>
/// One endpoint rather than an in-process scheduled timer: a timer is one more thing that runs
/// differently in a test host than in production, and the actual schedule — hourly, nightly,
/// whatever an operator picks — is a deployment concern, not a template default. A scheduled job
/// (a Kubernetes CronJob, a cloud scheduler) is expected to call this endpoint on that cadence.
/// </remarks>
[Route("api/v{version:apiVersion}/maintenance")]
[Asp.Versioning.ApiVersion("1.0")]
[Authorize(Policy = Policies.Administrator)]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
public sealed class MaintenanceController(
    IPurgeExpiredIdempotencyKeysUseCase purgeExpiredIdempotencyKeys,
    IPurgeExpiredRefreshTokensUseCase purgeExpiredRefreshTokens) : ApiControllerBase
{
    /// <summary>Deletes every idempotency key whose retention window has passed.</summary>
    [HttpDelete("idempotency-keys/expired")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PurgeResponse))]
    public async Task<ActionResult<PurgeResponse>> PurgeExpiredIdempotencyKeys(CancellationToken cancellationToken) =>
        OkOrProblem(
            MaintenanceMapping.ToPurgeResponse(await purgeExpiredIdempotencyKeys.ExecuteAsync(cancellationToken)));

    /// <summary>
    /// Deletes every refresh-token grant whose retention window has passed. Nothing a caller does
    /// triggers this: the table only grows, one row per rotation, until an operator prunes it.
    /// </summary>
    [HttpDelete("refresh-tokens/expired")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PurgeResponse))]
    public async Task<ActionResult<PurgeResponse>> PurgeExpiredRefreshTokens(CancellationToken cancellationToken) =>
        OkOrProblem(
            MaintenanceMapping.ToPurgeResponse(await purgeExpiredRefreshTokens.ExecuteAsync(cancellationToken)));
}
