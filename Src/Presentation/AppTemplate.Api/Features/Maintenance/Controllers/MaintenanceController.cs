using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands;
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
    IPurgeExpiredIdempotencyKeysUseCase purgeExpiredIdempotencyKeys) : ApiControllerBase
{
    /// <summary>Deletes every idempotency key whose retention window has passed.</summary>
    [HttpDelete("idempotency-keys/expired")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(int))]
    public async Task<ActionResult<int>> PurgeExpiredIdempotencyKeys(CancellationToken cancellationToken) =>
        OkOrProblem(await purgeExpiredIdempotencyKeys.ExecuteAsync(cancellationToken));
}
