# Security

This is a **template**. It is cloned, renamed and modified, so it has no released versions and no
patch stream: there is nothing here to which a CVE could be assigned, and nothing that reaches your
users except through your fork.

That shapes this document. It is not a promise that an application built from this template is
secure. It is an honest inventory of **what the template does**, and — the longer and more important
half — **what a deployment still has to do itself**. A control listed in the second section is not
a suggestion; it is a hole until you close it.

## Reporting a vulnerability

**In this template**, open a GitHub issue. There is no private disclosure channel and none is
warranted: the repository ships no running service and no binary anyone depends on, so an issue is
both faster and more useful than a private report.

**In an application you derived from it**, replace this section before you deploy. State a contact
address you actually monitor, an acknowledgement window, and whether you operate a safe-harbour
policy. A `SECURITY.md` inherited unchanged from a template tells a finder to file a public issue
against your production system.

## What the template provides

### Authentication and session handling
- **Default-deny authorisation.** A fallback policy requires an authenticated user, so an endpoint
  is protected unless it carries an explicit `[AllowAnonymous]`. Forgetting `[Authorize]` no longer
  publishes an endpoint.
- **Opaque, rotating refresh tokens**, never JWTs. Only a hash is stored. Presenting a token always
  consumes it, and replaying one that was already consumed **revokes the whole family** for that
  user. Consumption is a single conditional `UPDATE` — zero affected rows *is* the replay signal —
  so two simultaneous presentations cannot both succeed. See `docs/adr/0005`.
- **Account lockout** and a configurable password policy with a floor that configuration cannot
  lower. Email confirmation is required to sign in.
- **JWT validation** with issuer and audience always checked, a pinned algorithm list, zero clock
  skew, and security-stamp revalidation on every token — so a password change or a lockout takes
  effect before the access token would have expired.
- **Resistance to account enumeration.** Login does not distinguish an unknown user from a wrong
  password, *including in timing* — there is a deliberate decoy-hash path built from the configured
  hasher so both branches cost the same. Confirm-email answers identically for an unknown address
  and a bad token. Resend-confirmation answers identically whether or not the address exists.
- **Email confirmation is a POST with a JSON body**, not a GET with a query string, to keep a
  single-use token out of access logs, browser history and the `Referer` header.

### Transport and headers
- `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, and a
  default-deny `Content-Security-Policy`
  (`default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`).
- The `Server` and `X-Powered-By` headers are suppressed.
- Headers are written from a `Response.OnStarting` callback rather than set on the way in, because
  `UseExceptionHandler` clears the response before re-running the pipeline — eagerly-set headers
  would be dropped from exactly the 5xx responses that most need them.
- **`UseHttpsRedirection` is deliberately absent.** TLS terminates upstream and the container listens
  on plain 8080; a redirect would answer the orchestrator's health probe with a 307.
- **The application sends no HSTS header.** This is a decision, not an omission — see
  `docs/adr/0012`, and the deployment obligation below.
- **CORS denies by default.** An empty `Cors:AllowedOrigins` allows nothing rather than everything,
  and `AllowCredentials` is never set, because it is the combination of credentials and a permissive
  origin policy that turns CORS into a hole.

### Abuse resistance
- Rate limiting per client address: 10 requests/minute on the authentication endpoints, 300/minute
  globally, answering `429` with `Retry-After` and a ProblemDetails body.
- **This is only correct if `ReverseProxy` is configured — see the deployment obligations.**
- Bounded aggregates: a maximum number of items per list and tags per item, so one request cannot
  make the server do unbounded work.

### Data handling
- **No exception message ever reaches a client.** Every failure becomes an RFC 7807 ProblemDetails
  with a stable machine-readable `code`, and a `traceId` that joins to the server-side trace and log
  entry.
- **Ownership is a filter inside the read query**, not a check after fetching, so another user's
  resource is a `404` and never a `403` — a `403` would confirm the resource exists.
- **Optimistic concurrency** on writes via PostgreSQL's `xmin`, translated to a
  `ConcurrencyConflictException` and a `409`.
- **Conditional requests.** Every read of a `TodoList` or `TodoItem` publishes a strong `ETag`, and
  every write honours `If-Match` — a stale, malformed or unrecognised version is refused with `412`.
  `If-Match: *` and no `If-Match` header at all are covered by `docs/adr/0013`; see the known gap
  below for what stays your responsibility.
- **Request logging never touches the `Authorization` header, cookies, the query string or the body.**
  It logs a fixed field list, so a credential cannot arrive in a log by accident. The auth endpoints
  carry passwords and refresh tokens in their bodies, which is why body logging is not merely
  disabled but absent.

### Supply chain and configuration
- `NuGetAudit` at the `low` level with `NuGetAuditMode=all`, so **a known advisory in a direct or
  transitive package fails the build**, not just a report. CI checks it again independently.
- `Microsoft.OpenApi` is pinned to 2.7.6 because `Microsoft.AspNetCore.OpenApi` resolves 2.0.0,
  which carries GHSA-v5pm-xwqc-g5wc. Removing the pin fails `dotnet restore` outright.
- Every GitHub Action is pinned to a commit SHA, not a mutable tag.
- Dependabot watches NuGet, Actions, the Dockerfile and `docker-compose.yml`. Note that neither
  Docker ecosystem supports Dependabot *security* updates — for container images, the weekly version
  bump is the whole mechanism.
- **Every secret-shaped value in tracked configuration is an empty string**, and each section is
  bound to an options class with a validator and `ValidateOnStart()`. An incomplete or invalid
  configuration fails at **startup**, not on the first request. As shipped, `appsettings.json` alone
  will not boot the app — that is intentional.
- SMTP refuses any mode that can silently fall back to plaintext against a non-loopback host unless
  insecure transport is opted into explicitly, so an unencrypted mail path is always a visible,
  auditable choice.
- The container runs as a non-root user and exposes only `8080/tcp`; CI asserts both.
- **Migrations are not applied at startup outside Development** (`docs/adr/0009`).

## What a deployment must still do

### Required — the template is insecure without these

1. **Terminate TLS upstream and enforce HTTPS there.** The app serves plain HTTP by design.
2. **Send HSTS from the ingress.** The application will not do it. `max-age`, `includeSubDomains`
   and `preload` are domain-wide commitments that an application cannot know, and a template that
   shipped `preload` would burn a domain for months. Nothing here can detect that you forgot, which
   is exactly why it is first on this list.
3. **Configure the `ReverseProxy` section if anything sits in front of the app.** Until you do,
   `X-Forwarded-For` is ignored and the rate limiter partitions on the proxy's address — so **every
   caller in the world shares one 10-request window** and the brute-force protection does not work.
   Set `KnownProxies` and/or `KnownNetworks` to the hops you actually run, and `ForwardLimit` to
   their number. Do **not** enable it with empty trust lists to "make it work": ASP.NET Core treats
   two empty lists as *trust every caller*, which lets a client forge its own partition key and
   bypass the limiter just as completely. The options validator refuses to start in that state.
4. **Supply `Jwt:Key` from a secret manager**, with real entropy, and rotate it. Never from a file
   in source control. Same for `ConnectionStrings:Default` and the SMTP credentials.
5. **Set `Cors:AllowedOrigins`** to your real origins, and **narrow `AllowedHosts`** from `*` to the
   hostnames you serve.
6. **Apply migrations as a separate step** — the release workflow builds a self-contained bundle.
   Run it with a **migration-time principal that has DDL rights, and give the application's runtime
   user none**. The app should not be able to alter its own schema.
7. **Grant the database user least privilege**: DML on its own tables, no DDL, no superuser.

### Strongly recommended
- Ship the OTLP telemetry somewhere and alert on it — `429` and `401` rates are the signals that
  tell you brute force is happening.
- Set a log retention policy, and re-check redaction after you add any logging of your own. The
  guarantee here covers the middleware this template ships, not code you add.
- Back up the database and rehearse the restore.
- Put an authenticated gateway or network policy in front of `/health/ready` if the fact that your
  database is reachable is information you would rather not publish.
- Review the sample `TodoLists` feature out of your deployment if you do not need it.

## Known gaps

Stated here rather than left to be discovered. None of these is a hypothetical.

- **`If-Match` is optional by default.** Every read publishes a strong `ETag` and every write
  honours `If-Match`, refusing a stale or unrecognised version with `412`, but a request that sends
  no `If-Match` at all is still accepted unless `Concurrency:IfMatch` is set to `Required` — see
  `docs/adr/0013`. Until you set it, a slow user's form submission can still overwrite a change made
  after it was rendered without anything detecting it.
- **Domain-event consumers are isolated per event, not per consumer.** If a consumer of one event
  throws, later consumers *of that same event* do not run. The failure is logged and the committed
  transaction is correctly reported as committed, but the side effect is lost. This is the point at
  which the mechanism wants an outbox — if your consumers do anything a user would notice missing,
  add one before you rely on this.
- **The auth wire format has two owners.** `ConfigureJwtBearerOptions` in the Identity module builds
  its own `ProblemDetails` and owns the `auth.required` / `auth.forbidden` codes, so a change to how
  failures look must be made in two places.
- **Cancellation is not propagated through Identity.** `UserManager` and `SignInManager` accept no
  `CancellationToken`, so an abandoned request still runs its user-store I/O to completion.
- **No audit log of security-relevant events.** Logins, lockouts, token-family revocations and
  password changes are traced but not recorded in a queryable, tamper-evident store.
- **No multi-factor authentication.** ASP.NET Core Identity supports it; this template does not wire
  it up.
