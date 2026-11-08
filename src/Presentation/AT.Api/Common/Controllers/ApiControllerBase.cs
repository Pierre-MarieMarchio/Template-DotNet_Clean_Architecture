using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Controllers;

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

        return result.IsSuccess ? Serialised(result.Value) : result.Error!.ToActionResult(HttpContext);
    }

    private static OkObjectResult Serialised<TValue>(TValue value) => new(value) { DeclaredType = typeof(TValue) };

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

    protected ActionResult NoContentOrProblem(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult(HttpContext);
    }

    protected ActionResult<TValue> AcceptedOrProblem<TValue>(Result result, TValue body)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? new AcceptedResult { Value = body, DeclaredType = typeof(TValue) }
            : result.Error!.ToActionResult(HttpContext);
    }

    protected ActionResult CreatedOrProblem<TValue>(Result<TValue> result, string routeName, object routeValues)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? Located(routeName, routeValues, result.Value)
            : result.Error!.ToActionResult(HttpContext);
    }

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

    private CreatedAtRouteResult Located<TValue>(string routeName, object routeValues, TValue value)
    {
        var created = CreatedAtRoute(routeName, routeValues, value);
        created.DeclaredType = typeof(TValue);

        return created;
    }

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

    private bool IfMatchIsRequired() =>
        HttpContext.RequestServices.GetRequiredService<IOptions<ConcurrencyOptions>>().Value.IfMatch
            == IfMatchRequirement.Required;
}
