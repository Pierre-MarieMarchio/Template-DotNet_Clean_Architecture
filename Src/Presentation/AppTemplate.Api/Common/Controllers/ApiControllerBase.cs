using AppTemplate.Api.Common.Errors;
using AppTemplate.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Common.Controllers;

/// <summary>
/// Turns a <see cref="Result"/> into an HTTP response, so a controller action binds, calls one use
/// case and maps: no business logic, no try/catch, no hand-rolled error shapes.
/// </summary>
// Only genuinely universal statuses belong here: ProducesResponseType adds to an action's set
// and cannot be removed by it, so anything conditional has to be declared per action.
// 429 comes from the global rate limiter, which every endpoint passes through.
//
// No [Produces("application/json")]: it is a result filter that unconditionally overwrites
// ObjectResult.ContentTypes, including the "application/problem+json" that ErrorResults sets on
// every error response. System.Text.Json is the only output formatter registered, so a success
// response still negotiates to JSON without it.
[ApiController]
[ProducesResponseType(StatusCodes.Status429TooManyRequests, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ProblemDetails))]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<TValue> OkOrProblem<TValue>(Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Ok(result.Value) : result.Error!.ToActionResult();
    }

    protected ActionResult NoContentOrProblem(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    /// <summary>201 with a <c>Location</c> header, or the mapped problem response.</summary>
    protected ActionResult CreatedOrProblem<TValue>(Result<TValue> result, string routeName, object routeValues)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? CreatedAtRoute(routeName, routeValues, result.Value)
            : result.Error!.ToActionResult();
    }
}
