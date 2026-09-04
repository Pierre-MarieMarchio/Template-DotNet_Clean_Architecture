using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Idempotency;

/// <summary>
/// Makes a <see cref="IdempotentAttribute"/>-marked POST action safely retryable through an
/// <c>Idempotency-Key</c> header.
/// </summary>
/// <remarks>
/// <para>
/// A resource filter, not an action filter, for two reasons: it must run before model binding, so it
/// can buffer and hash the raw body while the stream is still untouched; and it must wrap result
/// execution, so it can observe the status and value the action actually produced.
/// </para>
/// <para>
/// <b>The capability is available, not compulsory.</b> An action carrying <see cref="IdempotentAttribute"/>
/// still behaves exactly as before for a caller that sends no <c>Idempotency-Key</c> header — nothing
/// here refuses that request. A deployment that wants the header to be mandatory can say so of its
/// own accord; that is not a decision this filter makes for it.
/// </para>
/// </remarks>
internal sealed class IdempotencyFilter(
    IIdempotencyStore store,
    IOptions<IdempotencyOptions> options,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    ILogger<IdempotencyFilter> logger) : IAsyncResourceFilter
{
    private const string _headerName = "Idempotency-Key";
    private const string _replayedHeaderName = "Idempotency-Replayed";

    /// <summary>Used only when MVC's own <see cref="JsonOptions"/> cannot be resolved.</summary>
    private static readonly JsonSerializerOptions _fallbackJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <see cref="ErrorType.Validation"/> rather than a new type: the same family as
    /// <see cref="IdempotencyErrors.KeyInvalid"/> — the header, as sent, cannot be honoured for this
    /// request — not a statement about the resource or the caller's authorisation to reach it.
    /// </summary>
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

        // No user to scope a key to. In practice the fallback authorisation policy has already
        // refused an unauthenticated request before a resource filter runs, so nothing reaches this
        // branch today — but a future caller ICurrentUser cannot resolve to a Guid (a machine caller
        // with no such subject, say) would otherwise lose the guarantee on every single request it
        // sends, silently, which is exactly backwards: that profile replays by design and needs this
        // protection the most. Refuse rather than proceed unprotected, so the day such a caller
        // exists its author has to decide how idempotency keys are scoped for it.
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

        var keyResult = IdempotencyKey.Create(userId, headerValues.ToString(), endpoint, fingerprint, settings.MaxKeyLength);

        if (keyResult.IsFailure)
        {
            context.Result = keyResult.Error!.ToActionResult(httpContext);
            return;
        }

        var key = keyResult.Value;
        var claim = await store.ClaimAsync(key, dateTimeProvider.UtcNow + settings.Retention, httpContext.RequestAborted);

        switch (claim.Outcome)
        {
            case IdempotencyOutcome.Replay:
                ShortCircuitReplay(context, claim.Response!);
                return;

            case IdempotencyOutcome.InProgress:
                context.Result = IdempotencyErrors.InProgress.ToActionResult(httpContext);
                return;

            case IdempotencyOutcome.KeyReused:
                context.Result = IdempotencyErrors.KeyReused.ToActionResult(httpContext);
                return;

            case IdempotencyOutcome.NotReplayable:
                context.Result = IdempotencyErrors.NotReplayable.ToActionResult(httpContext);
                return;

            case IdempotencyOutcome.Claimed:
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
            // The action never produced a response at all, so the corrected retry a caller sends
            // after fixing whatever threw must not find this key still held.
            await store.ReleaseAsync(key, CancellationToken.None);
            throw;
        }

        IdempotentResponse response;

        switch (executed.Result)
        {
            case ObjectResult { StatusCode: >= 200 and < 300 } objectResult:
                response = BuildResponse(httpContext, objectResult, settings.MaxStoredResponseBytes);
                break;

            // A NoContentResult (204) is a StatusCodeResult, not an ObjectResult: without this branch
            // every idempotent action that answers 204 fell into the "not worth replaying" case below
            // and had its key released, so a retry ran the write again for real.
            case StatusCodeResult { StatusCode: >= 200 and < 300 } statusCodeResult:
                response = BuildResponse(httpContext, statusCodeResult);
                break;

            default:
                // A non-2xx result (a validation failure, a conflict) is not something worth
                // replaying, and holding the claim would block a corrected retry under the same key.
                await store.ReleaseAsync(key, CancellationToken.None);
                return;
        }

        await store.CompleteAsync(key, response, CancellationToken.None);
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
        int maxStoredResponseBytes)
    {
        var jsonOptions = httpContext.RequestServices.GetService<IOptions<JsonOptions>>()
            ?.Value.JsonSerializerOptions
            ?? _fallbackJsonOptions;

        // The declared type, not the runtime type: a [JsonPolymorphic] discriminator is only written
        // when serialisation starts at the polymorphic base, and the replay below hands this string
        // straight to the client with no formatter left to fix it if the type were wrong here.
        string? body = result.Value is null
            ? null
            : JsonSerializer.Serialize(result.Value, result.DeclaredType ?? result.Value.GetType(), jsonOptions);

        if (body is not null && Encoding.UTF8.GetByteCount(body) > maxStoredResponseBytes)
        {
            // Dropped rather than truncated: a truncated JSON body is not valid JSON, and a replay
            // must either be the real response or an honest refusal, never a corrupted document.
            body = null;
        }

        return new IdempotentResponse(
            result.StatusCode ?? StatusCodes.Status200OK,
            body,
            ReadLocation(httpContext),
            ReadETag(httpContext));
    }

    /// <summary>Built for a status-code-only result (e.g. 204), which carries no body to store.</summary>
    private static IdempotentResponse BuildResponse(HttpContext httpContext, StatusCodeResult result) =>
        new(result.StatusCode, null, ReadLocation(httpContext), ReadETag(httpContext));

    private static string? ReadLocation(HttpContext httpContext) =>
        httpContext.Response.Headers.Location.Count > 0
            ? httpContext.Response.Headers.Location.ToString()
            : null;

    // Set by the action before this filter observes the result: a write that publishes an ETag does
    // so on Response.Headers directly, which is already populated by the time next() returns here.
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

        // Without this a replayed create or update would hand the caller a body with no validator
        // at all, leaving it unable to make the conditional request the ETag exists to support.
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
