# Request pipeline

Every request crosses the same pipeline, installed by one call, `UseHttpPipeline()`, in one order.
A feature adds nothing to it: an endpoint inherits the limits, the headers, the error format and the
authorisation rule by existing.

## The order

| Step | Why it sits here |
|---|---|
| Forwarded headers | first, before anything reads the client address or the scheme |
| Security headers, cache headers | written from `OnStarting`, so a response the exception handler, the rate limiter or a health endpoint writes carries them too |
| Request logging | outside the exception handler, so its entry reports the status the caller received |
| Request size limit | before anything reads the body; its `413` is still logged |
| Exception handler, status code pages | turn an exception or a bare status into a problem document |
| CORS | answers preflights before any work is done |
| Rate limiter | before the language and the timeout, so a rejected request does no work and starts no clock |
| Request language | sets the language of what the request writes |
| Request timeout | before authentication, so its deadline covers it |
<!--#if (!publicApi)-->
| Authentication, authorisation | in `Program.cs`, right after `UseHttpPipeline()` |
<!--#endif-->
<!--#if (publicApi)-->
| Authorisation | in `Program.cs`, right after `UseHttpPipeline()` |
<!--#endif-->

A host cannot insert a step in the middle. `RequestPipelineTests` holds the order: forwarded
headers first, logging ahead of the size limit, the pipeline before any middleware of the host's.

TLS terminates upstream. The API listens on plain HTTP on port 8080, installs no HTTPS redirection
and sends no HSTS header; [Deployment](../guides/deployment.md#behind-a-proxy) states what the
proxy in front must do, and ADR 0001 records the choice.

## Authorisation

<!--#if (accounts)-->
Authorisation is default-deny. A fallback policy requires an authenticated user, so an endpoint is
protected unless it opts out with `[AllowAnonymous]`. The two health endpoints opt out, and so, in
Development only, do the OpenAPI document and its reference page.
`DefaultDenyAuthorizationTests` reads every endpoint the host maps and expects `401` from each one
that does not opt out, without a token and with a garbage one.

<!--#endif-->
<!--#if (localAuth)-->
The sign-in, registration, refresh, logout, email confirmation and password reset actions are
anonymous too: each is where a caller holding no token of this API arrives.

<!--#endif-->
<!--#if (accounts)-->
`HttpSurfaceTests.EveryAnonymousEndpoint_IsOnTheList` holds the anonymous surface: a new anonymous
action is a line added to that test.

The fallback also applies when no endpoint matched, so an unknown route answers `401` to an anonymous
caller rather than `404`.

`AuthorizationPolicies.Administrator` is the one named policy: it requires the role the deployment
configures as the administrator's, and guards the maintenance and administration endpoints.
A new policy is registered beside it.
<!--#endif-->
<!--#if (publicApi)-->
This API authenticates nobody. The fallback policy admits every caller, and an action marked
`[Authorize]` is refused at start-up, naming the action, rather than failing at request time with no
scheme to challenge. An operation that needs a caller still fails: `ICurrentUser` answers no one,
and the use case answers `401` `auth.required`.
<!--#endif-->

## Validation

A request is refused on its way in at two points, with one code. Model binding refuses a value of
the wrong type or a body that is not valid JSON; the use case's FluentValidation validator refuses a
well-formed value that breaks a rule. Both answer `400` `request.validationFailed`, with the
offending fields under `errors`.

The JSON input formatter's own messages are switched off, so a malformed body never echoes a CLR
type name to the client. Validators measure strings after trimming, as the domain does.

## Problem details

Every refusal is an RFC 9457 problem document served as `application/problem+json`, with three
members a client can rely on:

| Member | Holds |
|---|---|
| `code` | the stable, dotted name of the situation; a client branches on it, never on `detail` |
| `traceId` | the identifier that joins the response to the server's log entry and trace |
| `type` | `ProblemTypes:BaseUri` followed by the code |

`ProblemDetailsNormaliser` is the single funnel: a `400` the framework produced before any code of
this project ran still gets the three members. The `413`, `429` and `504` written before MVC carry
them too, whatever the request's `Accept` header says. A `500` carries a fixed sentence and the
`traceId`, never an exception's text. The codes are tabled in
[the API reference](../reference/api.md#error-codes).

## Request logging

Each request writes one structured entry: method, path, status, elapsed time and the trace
identifiers. The entry never includes the `Authorization` header, cookies, the query string or the
body, so a credential cannot reach a log by accident. Logs are JSON from `AddJsonConsole`;
[Observability](../features/observability.md) covers their export.

## Rate limiting

A fixed window per client address, with no queue:

| Policy | Default | Applies to |
|---|---|---|
| Global | 300 requests per minute | every request but the health endpoints |
<!--#if (localAuth)-->
| `authentication` | 10 requests per minute | the authentication endpoints, on top of the global one |
<!--#endif-->

A refused request answers `429` with `Retry-After` and `rateLimit.exceeded`. The limiter runs before
authentication, so it partitions on the address, never on the user: validating a bearer on every
request it was about to reject would amplify the load it exists to shed. Callers sharing an address
share a budget, and behind a proxy the address is right only once `ReverseProxy` names the proxy.

The counters live in each instance's memory, so the limit a caller meets is multiplied by the
replica count. The numbers are the `RateLimiting` section of
[the configuration reference](../reference/configuration.md).

## Body size and timeouts

A body is at most `RequestLimits:MaxRequestBodyBytes`, 64 KiB by default. A middleware refuses a
declared length above it with `413` `request.tooLarge` before the body is read, and Kestrel's own
limit, set from the same value, stops a chunked body that declares none.
<!--#if (files)-->
The file upload endpoint alone raises the limit to `Files:ApiUploadMaxBytes` plus 16 KiB of
multipart framing.
<!--#endif-->

Every endpoint runs under `RequestTimeouts:Default`, five minutes by default. A request still
unanswered at the deadline gets `504` `request.timeout`; one the client abandons is logged as `499`
`request.cancelled`. The default outlasts the database's own worst case, a command timeout retried
by the execution strategy, so the request never cancels a write that is still safely retrying. An
endpoint whose work runs longer names its own policy with `[RequestTimeout]`; a streaming endpoint
uses `[DisableRequestTimeout]`.

## CORS and security headers

CORS denies by default: an empty `Cors:AllowedOrigins` allows no origin. A listed origin may send
any header and method, can read `Retry-After`, `ETag`, `Location` and `Idempotency-Replayed`, and
caches its preflight for ten minutes. Credentials are never allowed: the combination of credentials
and a permissive origin is what turns CORS into a hole.
<!--#if (localAuth)-->
The refresh-token cookie depends on that: no other origin can send the custom header that unlocks
it with the cookie attached.
<!--#endif-->

Every response carries `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
`X-Frame-Options: DENY` and a default-deny `Content-Security-Policy`, and no `Server` or
`X-Powered-By` header. In Development, the reference page under `/scalar` gets a wider policy, with a
nonce for its one inline script; the API's own policy is not weakened for it.

## Language of the answer

The request's `Accept-Language` chooses the language of what the request writes, such as a mail.
The header is parsed by hand: the well-formed tag with the highest `q` wins, a missing `q` counts as
1, `q=0` excludes a tag, ties keep the header's order, and a malformed entry is skipped whole.

The chosen value is a BCP 47 tag in `CurrentLanguage.Tag`, not a culture: the project builds with
invariant globalization, so the tag chooses which text is written and nothing else, and every value
parses the same way for every caller. With no usable header, and always in a background job, the
language is `Localization:DefaultCulture`. Problem documents are written in English.

## Decisions

- [0001. Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): TLS upstream, FluentValidation, the language as a tag.
