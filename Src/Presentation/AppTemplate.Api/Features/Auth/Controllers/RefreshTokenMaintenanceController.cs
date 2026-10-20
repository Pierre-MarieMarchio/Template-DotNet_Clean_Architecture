using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Maintenance.Contracts.Mapping;
using AppTemplate.Api.Features.Maintenance.Contracts.Responses;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Auth.Controllers;

[Route("api/v{version:apiVersion}/maintenance")]
[Asp.Versioning.ApiVersion("1.0")]
[Tags("Maintenance")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
public sealed class RefreshTokenMaintenanceController(
    IPurgeExpiredRefreshTokensUseCase purgeExpiredRefreshTokens) : ApiControllerBase
{
    /// <summary>Deletes every refresh-token grant whose retention window has passed.</summary>
    [HttpDelete("refresh-tokens/expired")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PurgeResponse))]
    public async Task<ActionResult<PurgeResponse>> PurgeExpiredRefreshTokens(CancellationToken cancellationToken) =>
        OkOrProblem(
            MaintenanceResponseMapping.ToPurgeResponse(await purgeExpiredRefreshTokens.ExecuteAsync(cancellationToken)));
}
