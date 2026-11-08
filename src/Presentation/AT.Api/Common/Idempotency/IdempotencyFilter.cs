using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Idempotency;

internal sealed class IdempotencyFilter(
    IIdempotencyStore store,
    IOptions<IdempotencyOptions> options,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<IdempotencyFilter> logger) : IAsyncResourceFilter
{
    private const string _headerName = "Idempotency-Key";
    private const string _replayedHeaderName = "Idempotency-Replayed";

    private static readonly JsonSerializerOptions _fallbackJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Error _callerNotIdentifiable = Error.Validation(
        "idempotency.callerNotIdentifiable",
        "The 'Idempotency-Key' header requires a caller this server can identify, and this request carries none.");

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var settings = options.Value;
        var httpContext = context.HttpContext;

        if (!settings.Enabled
            || !HttpMethods.IsPost(httpContext.Request.Method)
            || !HasIdempotentAttribute(context)
            || !httpContext.Request.Headers.TryGetValue(_headerName, out var headerValues))
        {
            await next();
            return;
        }

        if (currentUser.UserId is not { } userId)
        {
            logger.LogWarning(
                "Idempotency-Key on {Method} {Path} was refused: the caller carries no identity to scope the key to.",
                httpContext.Request.Method,
                httpContext.Request.Path);

            context.Result = _callerNotIdentifiable.ToActionResult(httpContext);
            return;
        }

        string endpoint = $"{httpContext.Request.Method} {httpContext.Request.Path}";
        string fingerprint = await ComputeFingerprintAsync(httpContext, endpoint);

        var keyResult = IdempotencyKey.Create(userId.Value, headerValues.ToString(), endpoint, fingerprint, settings.MaxKeyLength);

        if (keyResult.IsFailure)
        {
            context.Result = keyResult.Error!.ToActionResult(httpContext);
            return;
        }

        var key = keyResult.Value;
        var now = timeProvider.GetUtcNow();
        var claim = await store.ClaimAsync(key, now + settings.Retention, now + settings.ClaimLease, httpContext.RequestAborted);

        switch (claim.Status)
        {
            case IdempotencyStatus.Replay:
                ShortCircuitReplay(context, claim.Response!);
                return;

            case IdempotencyStatus.InProgress:
                context.Result = IdempotencyErrors.InProgress.ToActionResult(httpContext);
                return;

            case IdempotencyStatus.KeyReused:
                context.Result = IdempotencyErrors.KeyReused.ToActionResult(httpContext);
                return;

            case IdempotencyStatus.NotReplayable:
                context.Result = IdempotencyErrors.NotReplayable.ToActionResult(httpContext);
                return;

            case IdempotencyStatus.Claimed:
            default:
                break;
        }

        ResourceExecutedContext executed;

        try
        {
            executed = await next();
        }
        catch
        {
            await ReleaseUnlessStartedAsync(key, httpContext);
            throw;
        }

        if (executed is { Exception: not null, ExceptionHandled: false })
        {
            await ReleaseUnlessStartedAsync(key, httpContext);
            return;
        }

        if (SuccessStatusCode(executed) is not { } statusCode)
        {
            await store.ReleaseAsync(key, CancellationToken.None);
            return;
        }

        var response = executed.Result is ObjectResult objectResult
            ? BuildResponse(httpContext, objectResult, statusCode, settings.MaxStoredResponseBytes)
            : new IdempotentResponse(statusCode, null, ReadLocation(httpContext), ReadETag(httpContext));

        await store.CompleteAsync(key, response, CancellationToken.None);
    }

    private async Task ReleaseUnlessStartedAsync(IdempotencyKey key, HttpContext httpContext)
    {
        if (!httpContext.Response.HasStarted)
        {
            await store.ReleaseAsync(key, CancellationToken.None);
        }
    }

    private static int? SuccessStatusCode(ResourceExecutedContext executed)
    {
        if (executed.Result is null)
        {
            return null;
        }

        int statusCode = executed.Result is IStatusCodeActionResult { StatusCode: { } declared }
            ? declared
            : executed.HttpContext.Response.StatusCode;

        return statusCode is >= 200 and < 300 ? statusCode : null;
    }

    private static bool HasIdempotentAttribute(ResourceExecutingContext context) =>
        context.ActionDescriptor.EndpointMetadata.OfType<IdempotentAttribute>().Any();

    private static async Task<string> ComputeFingerprintAsync(HttpContext httpContext, string endpoint)
    {
        httpContext.Request.EnableBuffering();

        using var reader = new StreamReader(
            httpContext.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        string body = await reader.ReadToEndAsync(httpContext.RequestAborted);
        httpContext.Request.Body.Position = 0;

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint + "\n" + body));

        return Convert.ToHexStringLower(hash);
    }

    private static IdempotentResponse BuildResponse(
        HttpContext httpContext,
        ObjectResult result,
        int statusCode,
        int maxStoredResponseBytes)
    {
        var jsonOptions = httpContext.RequestServices.GetService<IOptions<JsonOptions>>()
            ?.Value.JsonSerializerOptions
            ?? _fallbackJsonOptions;

        string? body = result.Value is null
            ? null
            : JsonSerializer.Serialize(result.Value, result.DeclaredType ?? result.Value.GetType(), jsonOptions);

        if (body is not null && Encoding.UTF8.GetByteCount(body) > maxStoredResponseBytes)
        {
            body = null;
        }

        return new IdempotentResponse(statusCode, body, ReadLocation(httpContext), ReadETag(httpContext));
    }

    private static string? ReadLocation(HttpContext httpContext) =>
        httpContext.Response.Headers.Location.Count > 0
            ? httpContext.Response.Headers.Location.ToString()
            : null;

    private static string? ReadETag(HttpContext httpContext) =>
        httpContext.Response.Headers.ETag.Count > 0
            ? httpContext.Response.Headers.ETag.ToString()
            : null;

    private static void ShortCircuitReplay(ResourceExecutingContext context, IdempotentResponse response)
    {
        var httpResponse = context.HttpContext.Response;

        httpResponse.Headers[_replayedHeaderName] = "true";

        if (response.Location is not null)
        {
            httpResponse.Headers.Location = response.Location;
        }

        if (response.ETag is not null)
        {
            httpResponse.Headers.ETag = response.ETag;
        }

        context.Result = response.Body is null
            ? new StatusCodeResult(response.StatusCode)
            : new ContentResult
            {
                StatusCode = response.StatusCode,
                Content = response.Body,
                ContentType = "application/json; charset=utf-8",
            };
    }
}
