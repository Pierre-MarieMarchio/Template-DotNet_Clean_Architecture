using AppTemplate.Api.Common.Controllers;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.ExternalIdentity.Controllers;

[Route("api/v{version:apiVersion}/external-identities")]
[Asp.Versioning.ApiVersion("1.0")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
public sealed class ExternalIdentityAdministrationController(
    IExternalIdentityService externalIdentities) : ApiControllerBase
{
    /// <summary>
    /// Deletes the account's local presence: every mapping that names it, and its assigned roles.
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Delete(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(
            await externalIdentities.DeleteAsync(UserId.Create(userId), cancellationToken));
}
