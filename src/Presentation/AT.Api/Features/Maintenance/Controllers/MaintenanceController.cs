using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Features.Maintenance.Contracts.Mapping;
using AppTemplate.Api.Features.Maintenance.Contracts.Responses;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Presentation.Core.Common.Security;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Maintenance.Controllers;

[Route("api/v{version:apiVersion}/maintenance")]
[Asp.Versioning.ApiVersion("1.0")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
public sealed class MaintenanceController(
    ISender sender) : ApiControllerBase
{
    /// <summary>Deletes every idempotency key whose retention window has passed.</summary>
    [HttpDelete("idempotency-keys/expired")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PurgeResponse))]
    public async Task<ActionResult<PurgeResponse>> PurgeExpiredIdempotencyKeys(CancellationToken cancellationToken) =>
        OkOrProblem(
            MaintenanceResponseMapping.ToPurgeResponse(await sender.Send(new PurgeExpiredIdempotencyKeysCommand(), cancellationToken)));
}
