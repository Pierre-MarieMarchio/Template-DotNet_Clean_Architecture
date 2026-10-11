using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Domain.Core.Common.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.ExternalIdentity.Controllers;

/// <summary>
/// Acting on the local half of an externally-authenticated account. This API holds no passwords and
/// mints no tokens — the provider does both — so the one administrative act left here is deleting an
/// account's presence: its mappings and the roles assigned to them.
/// </summary>
/// <remarks>
/// Everything here is restricted to <see cref="AuthorizationPolicies.Administrator"/> on the class:
/// nothing on this surface has a self-service counterpart, so there is no accidental
/// <c>[AllowAnonymous]</c> for a class-level policy to defeat. No
/// authentication budget: nothing here handles a credential, and every request is already behind
/// an access token and the Administrator policy.
/// <para>
/// The deletion the provider itself performs has no signal here, deliberately: when the provider
/// retires a subject, an administrator calls this with the owner id, or the project wires the
/// provider's own webhook to the same service. See <c>docs/ARCHITECTURE.md</c>.
/// </para>
/// </remarks>
[Route("api/v{version:apiVersion}/external-identities")]
[Asp.Versioning.ApiVersion("1.0")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
public sealed class ExternalIdentityAdministrationController(
    IExternalIdentityService externalIdentities) : ApiControllerBase
{
    /// <summary>
    /// Deletes the account's local presence: every mapping that names the owner, its assigned
    /// roles with them. The data the features hold under the id is removed through the
    /// account-deleted fact each consumes — see <c>ExternalIdentityService.DeleteAsync</c>.
    /// </summary>
    /// <remarks>
    /// A caller who signs in again is provisioned under a new owner id, with none of the deleted
    /// one's data: that is what deleting the account meant, and the provider's own account is the
    /// provider's to delete.
    /// </remarks>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Delete(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(
            await externalIdentities.DeleteAsync(UserId.Create(userId), cancellationToken));
}
