using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Concurrency;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Controllers;

/// <summary>
/// Turns a <see cref="Result"/> into an HTTP response, so a controller action binds, calls one use
/// case and maps: no business logic, no try/catch, no hand-rolled error shapes.
/// </summary>
// Only genuinely universal statuses belong here: ProducesResponseType adds to an action's set
// and cannot be removed by it, so anything conditional has to be declared per action.
// 429 comes from the global rate limiter, which every endpoint passes through; 413 and 415 are
// reachable at the same layer, ahead of every action, for the same reason.
//
// No [Produces("application/json")]: it is a result filter that unconditionally overwrites
// ObjectResult.ContentTypes, including the "application/problem+json" that ErrorResults sets on
// every error response. System.Text.Json is the only output formatter registered, so a success
// response still negotiates to JSON without it.
[ApiController]
[ProducesResponseType(StatusCodes.Status413PayloadTooLarge, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status415UnsupportedMediaType, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status429TooManyRequests, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ProblemDetails))]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<TValue> OkOrProblem<TValue>(Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Ok(result.Value) : result.Error!.ToActionResult(HttpContext);
    }

    /// <summary>
    /// Publishes the aggregate's version as a strong <c>ETag</c> and answers 304 when the caller's
    /// <c>If-None-Match</c> already names it.
    /// </summary>
    /// <remarks>
    /// The header is written before the status is chosen: RFC 9110 requires a 304 to carry the
    /// validator it is refusing to resend the body for.
    /// </remarks>
    protected ActionResult<TValue> OkOrProblem<TValue>(Result<Versioned<TValue>> result) where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.Error!.ToActionResult(HttpContext);
        }

        string tag = EntityTagValue.From(result.Value.Version);
        Response.Headers.ETag = tag;

        return IfNoneMatchPrecondition.Matches(Request, tag)
            ? StatusCode(StatusCodes.Status304NotModified)
            : Ok(result.Value.Value);
    }

    /// <summary>200 with the updated representation and its new <c>ETag</c>, or the mapped problem.</summary>
    protected ActionResult<TValue> UpdatedOrProblem<TValue>(Result<Versioned<TValue>> result) where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.Error!.ToActionResult(HttpContext);
        }

        Response.Headers.ETag = EntityTagValue.From(result.Value.Version);

        return Ok(result.Value.Value);
    }

    protected ActionResult NoContentOrProblem(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult(HttpContext);
    }

    /// <summary>201 with a <c>Location</c> header, or the mapped problem response.</summary>
    protected ActionResult CreatedOrProblem<TValue>(Result<TValue> result, string routeName, object routeValues)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? CreatedAtRoute(routeName, routeValues, result.Value)
            : result.Error!.ToActionResult(HttpContext);
    }

    /// <summary>
    /// 201 with the created representation, its <c>ETag</c>, and a <c>Location</c> built only on
    /// success: <paramref name="routeValues"/> is a function precisely so nothing has to be evaluated
    /// — or defaulted — against a value a failed result never produced.
    /// </summary>
    protected ActionResult<TValue> CreatedOrProblem<TValue>(
        Result<Versioned<TValue>> result,
        string routeName,
        Func<TValue, object> routeValues) where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(routeName);
        ArgumentNullException.ThrowIfNull(routeValues);

        if (result.IsFailure)
        {
            return result.Error!.ToActionResult(HttpContext);
        }

        Response.Headers.ETag = EntityTagValue.From(result.Value.Version);

        return CreatedAtRoute(routeName, routeValues(result.Value.Value), result.Value.Value);
    }

    /// <summary>
    /// Reads the request's <c>If-Match</c> header and decides transport in one step: whether the
    /// value is well-formed, and whether a missing one is allowed by
    /// <see cref="ConcurrencyOptions.IfMatch"/>. <paramref name="precondition"/> is the version set
    /// the caller named, if any; <paramref name="requiresExistence"/> is true for an <c>If-Match: *</c>,
    /// which asserts that the resource exists without naming a version.
    /// </summary>
    /// <returns><c>null</c> when the request may proceed; otherwise the response to return as-is.</returns>
    protected ActionResult? ReadPrecondition(out VersionPrecondition? precondition, out bool requiresExistence)
    {
        var ifMatch = IfMatchPrecondition.Read(Request);

        requiresExistence = ifMatch.State == IfMatchState.Any;
        precondition = ifMatch.Required;

        return ifMatch.State switch
        {
            IfMatchState.Malformed => PreconditionProblems.Malformed.ToActionResult(HttpContext),
            IfMatchState.Absent when IfMatchIsRequired() => PreconditionProblems.Required.ToActionResult(HttpContext),
            _ => null,
        };
    }

    /// <summary>
    /// <c>If-Match: *</c> asserts that the resource exists, so a not-found result is that condition
    /// failing rather than a plain 404 — the caller gets 412, not 404, for a list that never existed
    /// under the id it named unconditionally.
    /// </summary>
    protected static Result RequiringExistence(bool requiresExistence, Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return requiresExistence && result.Error?.Type == ErrorType.NotFound
            ? Result.Failure(ConcurrencyErrors.PreconditionFailed)
            : result;
    }

    protected static Result<TValue> RequiringExistence<TValue>(bool requiresExistence, Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return requiresExistence && result.Error?.Type == ErrorType.NotFound
            ? Result.Failure<TValue>(ConcurrencyErrors.PreconditionFailed)
            : result;
    }

    // A base class cannot demand IOptions<ConcurrencyOptions> as a constructor parameter without
    // forcing it on every controller that derives from it, present and future, whether or not that
    // controller ever calls ReadPrecondition. Resolving it here, once, from the request's own
    // container is the one place in this project a service locator is justified.
    private bool IfMatchIsRequired() =>
        HttpContext.RequestServices.GetRequiredService<IOptions<ConcurrencyOptions>>().Value.IfMatch
            == IfMatchRequirement.Required;
}
