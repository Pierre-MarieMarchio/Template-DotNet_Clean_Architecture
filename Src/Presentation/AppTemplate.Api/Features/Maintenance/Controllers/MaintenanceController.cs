using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Core.Common.Controllers;
using AppTemplate.Api.Features.Maintenance.Contracts.Responses;
using AppTemplate.Api.Features.Maintenance.Mapping;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.PurgeExpiredRefreshTokens;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Maintenance.Controllers;

/// <summary>
/// Administrative operations with no user-facing counterpart.
/// </summary>
/// <remarks>
/// Both purges here are also run by the worker's maintenance loop. This endpoint is what a
/// scheduler outside the process calls — a Kubernetes CronJob, a cloud scheduler.
/// </remarks>
[Route("api/v{version:apiVersion}/maintenance")]
[Asp.Versioning.ApiVersion("1.0")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
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
            MaintenanceResponseMapping.ToPurgeResponse(await purgeExpiredIdempotencyKeys.ExecuteAsync(cancellationToken)));

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
