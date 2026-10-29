using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Mapping;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.CreateAccessKey;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.RevokeAccessKey;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Queries.ListAccessKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

[Route("api/v{version:apiVersion}/auth/access-keys")]
[Asp.Versioning.ApiVersion("1.0")]
[Authorize]
public sealed class AccessKeysController(
    ICreateAccessKeyUseCase createAccessKey,
    IListAccessKeysUseCase listAccessKeys,
    IRevokeAccessKeyUseCase revokeAccessKey) : ApiControllerBase
{
    /// <summary>
    /// Creates an access key for a host whose callers cannot refresh a token, re-authenticating the
    /// caller; the response carries the key, once. This API never accepts one as a credential.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(CreatedAccessKeyResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<CreatedAccessKeyResponse>> CreateAccessKey(
        [FromBody] CreateAccessKeyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateAccessKeyCommand(
            request.Name,
            request.Scopes,
            request.ExpiresAt,
            request.CurrentPassword);

        return CreatedOrProblem(
            AccessKeyResponseMapping.ToCreatedAccessKeyResponse(await createAccessKey.ExecuteAsync(command, cancellationToken)),
            nameof(ListAccessKeys),
            new { });
    }

    /// <summary>Lists the caller's live access keys, newest first, without their secrets.</summary>
    [HttpGet(Name = nameof(ListAccessKeys))]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AccessKeysResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<AccessKeysResponse>> ListAccessKeys(CancellationToken cancellationToken) =>
        OkOrProblem(AccessKeyResponseMapping.ToAccessKeysResponse(await listAccessKeys.ExecuteAsync(cancellationToken)));

    /// <summary>Revokes one of the caller's access keys by the id the access key list names.</summary>
    [HttpDelete("{accessKeyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> RevokeAccessKey(Guid accessKeyId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await revokeAccessKey.ExecuteAsync(new RevokeAccessKeyCommand(accessKeyId), cancellationToken));
}
