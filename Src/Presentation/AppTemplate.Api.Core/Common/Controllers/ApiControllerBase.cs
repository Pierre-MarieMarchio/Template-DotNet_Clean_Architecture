using AppTemplate.Api.Core.Common.Concurrency;
using AppTemplate.Api.Core.Common.Errors;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Core.Common.Controllers;

/// <summary>
/// Turns a <see cref="Result"/> into an HTTP response, so a controller action binds, calls one use
/// case and maps: no business logic, no try/catch, no hand-rolled error shapes.
/// </summary>
// Only universal statuses: ProducesResponseType adds to an action's set and cannot be removed by
// it. 429, 413 and 415 all arrive ahead of every action.
//
// No [Produces("application/json")]: it overwrites ObjectResult.ContentTypes, including the
// "application/problem+json" ErrorMapping sets on every error response.
[ApiController]
[ProducesResponseType(StatusCodes.Status413PayloadTooLarge, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status415UnsupportedMediaType, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status429TooManyRequests, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ProblemDetails))]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Answers 200 with the value, or the failure's problem details. A versioned value also gets
    /// its <c>ETag</c>, so a caller can send it back as <c>If-Match</c> on the next write.
    /// </summary>
    /// <typeparam name="TValue">What the use case returned.</typeparam>
    /// <param name="result">The use case's outcome.</param>
    /// <returns>The value, or the mapped problem details.</returns>
    protected ActionResult<TValue> OkOrProblem<TValue>(Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Serialised(result.Value) : result.Error!.ToActionResult(HttpContext);
    }

    /// <summary>
    /// 200, serialised as <typeparamref name="TValue"/> rather than as whatever the value's runtime
    /// type happens to be.
    /// </summary>
    /// <remarks>
    /// <c>Ok(value)</c> leaves <see cref="ObjectResult.DeclaredType"/> null, and the output formatter
    /// then serialises the runtime type. For a closed hierarchy that is silently wrong: a
    /// <c>[JsonPolymorphic]</c> discriminator is written only when serialisation starts at the
    /// polymorphic base, so the derived branch goes out with no <c>status</c> on it at all and no
    /// client can tell the branches apart. Naming the declared type here is what puts it back.
    /// </remarks>
    private static OkObjectResult Serialised<TValue>(TValue value) => new(value) { DeclaredType = typeof(TValue) };

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

        string tag = EntityTagMapping.From(result.Value.Version);
        Response.Headers.ETag = tag;

        return IfNoneMatchPrecondition.Matches(Request, tag)
            ? StatusCode(StatusCodes.Status304NotModified)
            : Serialised(result.Value.Value);
    }

    /// <summary>200 with the updated representation and its new <c>ETag</c>, or the mapped problem.</summary>
    protected ActionResult<TValue> UpdatedOrProblem<TValue>(Result<Versioned<TValue>> result) where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.Error!.ToActionResult(HttpContext);
        }

        Response.Headers.ETag = EntityTagMapping.From(result.Value.Version);

        return Serialised(result.Value.Value);
    }

    /// <summary>
    /// Answers 204 on success, or the failure's problem details.
    /// </summary>
    /// <param name="result">The use case's outcome.</param>
    /// <returns>No content, or the mapped problem details.</returns>
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
            ? Located(routeName, routeValues, result.Value)
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

        Response.Headers.ETag = EntityTagMapping.From(result.Value.Version);

        return Located(routeName, routeValues(result.Value.Value), result.Value.Value);
    }

    /// <summary>201 with a <c>Location</c>, serialised as <typeparamref name="TValue"/>.</summary>
    /// <remarks>Declares the type for the reason <see cref="Serialised{TValue}"/> gives.</remarks>
    private CreatedAtRouteResult Located<TValue>(string routeName, object routeValues, TValue value)
    {
        var created = CreatedAtRoute(routeName, routeValues, value);
        created.DeclaredType = typeof(TValue);

        return created;
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
            IfMatchState.Malformed => PreconditionErrors.Malformed.ToActionResult(HttpContext),
            IfMatchState.Absent when IfMatchIsRequired() => PreconditionErrors.Required.ToActionResult(HttpContext),
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

    /// <summary>
    /// Turns "not found" into "precondition failed" when the caller demanded the resource exist.
    /// </summary>
    /// <typeparam name="TValue">What the use case returned.</typeparam>
    /// <param name="requiresExistence">
    /// Whether the request carried a precondition that only an existing resource can satisfy.
    /// </param>
    /// <param name="result">The use case's outcome.</param>
    /// <returns>
    /// The same result, or a precondition failure when absence is what broke the precondition.
    /// </returns>
    protected static Result<TValue> RequiringExistence<TValue>(bool requiresExistence, Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return requiresExistence && result.Error?.Type == ErrorType.NotFound
            ? Result.Failure<TValue>(ConcurrencyErrors.PreconditionFailed)
            : result;
    }

    // Demanding IOptions<ConcurrencyOptions> in the constructor would force it on every derived
    // controller, including those that never call ReadPrecondition.
    private bool IfMatchIsRequired() =>
        HttpContext.RequestServices.GetRequiredService<IOptions<ConcurrencyOptions>>().Value.IfMatch
            == IfMatchRequirement.Required;
}
