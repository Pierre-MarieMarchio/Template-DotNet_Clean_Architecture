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
- **Default-deny authorisation.** `Program.cs` sets `AuthorizationOptions.FallbackPolicy` to one
  requiring an authenticated user, so an endpoint is protected unless it carries an explicit
  `[AllowAnonymous]`. Forgetting `[Authorize]` does not publish an endpoint. The policy also
  applies where no endpoint matched, so an unknown route answers `401` to an anonymous caller.
- **Opaque, rotating refresh tokens**, never JWTs. Only a hash is stored. Presenting a token always
  consumes it, and replaying one that was already consumed **revokes the whole family** for that
  user. `RefreshTokenTable` does the consumption as a single conditional `UPDATE` — zero affected
  rows *is* the replay signal — so two simultaneous presentations cannot both succeed.
- **Account lockout** and a configurable password policy with a floor that configuration cannot
  lower. Email confirmation is required to sign in.
- **JWT validation** with issuer and audience always checked, a pinned algorithm list
  (`ConfigureJwtBearerOptions` sets `ValidAlgorithms = [HmacSha256]`, matching what
  `AccessTokenIssuer` signs with), thirty seconds of clock skew, and security-stamp revalidation on
  every token — so a password change or a
  lockout takes effect before the access token would have expired. The skew is small and
  deliberately not zero: the issuer stamps the token from the injected clock while validation reads
  the machine's, so at zero tolerance one backward step — an NTP correction, a resumed VM — refuses
  every token in circulation at once. It is far below the framework's five-minute default.
- **Resistance to account enumeration.** Login does not distinguish an unknown user from a wrong
  password, *including in timing* — `UserAccountsService` runs a deliberate decoy-hash path built
  from the configured hasher, so both branches cost the same. Confirm-email answers identically for
  an unknown address and a bad token. Resend-confirmation answers identically whether or not the
  address exists.
- **Email confirmation is a POST with a JSON body**, not a GET with a query string, to keep a
  single-use token out of access logs, browser history and the `Referer` header.
- **Two-factor sign-in via an authenticator app (TOTP)**, built entirely on ASP.NET Core Identity's
  own primitives — no hand-rolled RFC 6238. A confirmed second factor mints ten single-use recovery
  codes, shown once. A password that matches a two-factor account still does not sign in on its own:
  `/login` answers with a short-lived challenge token instead of a token pair, and a second call
  exchanges that token plus a code — from the authenticator app or a recovery code — for the pair.
  It is neither a JWT nor a data-protected token, and could not be: both carry their own validity
  and are accepted on the strength of a signature, whereas this one has to be revocable while it is
  still in date — spent by the redemption that succeeds, destroyed by the attempts that fail. That
  makes server-side state the requirement rather than the implementation detail.
  The challenge is spent by a successful redemption, and **bounded on failure**: it tolerates
  `TwoFactor:MaxChallengeAttempts` wrong codes (five by default) and is then destroyed, so the
  password has to be presented again. That counter is the only thing that bounds guessing a code —
  account lockout counts failed *password* checks and presenting a code is not one — and without it
  a caller who already had the password could offer codes for the whole challenge lifetime, stopped
  only by a rate limiter that is per process and therefore per replica. Spending an attempt rewrites
  the challenge without moving its deadline, so guessing cannot keep one alive. The challenge is
  never a bearer credential by itself and is stored server-side, keyed by
  account, so it is redeemable by any replica behind the load balancer and survives a redeploy.
  Enabling or disabling the second factor rotates the security stamp and revokes every refresh token
  for the account, exactly like a password change — **and requires the current password on both
  sides of that symmetry**: arming the second factor is a security-posture change that costs every
  other session exactly as disarming it does, so a stolen access token alone can do neither.
- **An administrative escape hatch for a second factor nobody can prove possession of any more**: an
  account that has lost the authenticator app *and* its recovery codes cannot complete `/login` at
  all, and had no recourse short of deleting the account outright. An administrator can strip that
  account's second factor directly, on the strength of the `Administrator` policy rather than that
  account's own credential — and is refused with a 403 against their own account, so a stolen
  administrator session cannot use this route to do to itself what the symmetry above exists to
  prevent a stolen session doing.

### Transport and headers

`Common/Security/SecurityHeadersExtensions.cs` holds all of the following, and
`Common/Security/CorsExtensions.cs` the last one.

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
  the deployment obligation below: only the component terminating TLS knows the domain, its
  subdomains and the certificate, so only it can promise what HSTS promises.
- **CORS denies by default.** An empty `Cors:AllowedOrigins` allows nothing rather than everything,
  and `AllowCredentials` is never set, because it is the combination of credentials and a permissive
  origin policy that turns CORS into a hole.

### Abuse resistance
- Rate limiting per client address: 10 requests/minute on the authentication endpoints, 300/minute
  globally, answering `429` with `Retry-After` and a ProblemDetails body. The two numbers are
  `RateLimitingExtensions.AuthenticationPermitLimit` and `.GlobalPermitLimit`, over the
  one-minute `RateLimiterWindow.Default`.
- **This is only correct if `ReverseProxy` is configured — see the deployment obligations.**
- Bounded aggregates: a maximum number of items per list and tags per item, so one request cannot
  make the server do unbounded work.
- **An oversized body is refused before it is buffered, and the refusal is logged.**
  `UseApiRequestLimits` answers `413` without calling the next middleware, which would ordinarily
  make the rejection invisible — so `Program.cs` registers `UseApiRequestLogging` *ahead* of it,
  and every `413` produces a log entry reporting the status the caller received. A flood of them
  is the signal that someone is probing the limit, and it would otherwise be the one class of
  refusal that left no trace.

### Data handling
- **No exception message ever reaches a client**, and it takes two mechanisms rather than one,
  because there are two ways a message gets out. `GlobalExceptionHandler` closes the first: an
  unhandled exception becomes an RFC 7807 ProblemDetails with a fixed sentence, a stable
  machine-readable `code` and a `traceId` that joins to the server-side trace and log entry —
  never the exception's own text. `builder.Services.Configure<Mvc.JsonOptions>(o =>
  o.AllowInputFormatterExceptionMessages = false)` in `Program.cs` closes the second: left on, the
  JSON input formatter copies a `JsonException`'s text into the model error, and that text names
  the CLR type being bound and the byte offset it stopped at. Turned off, the model error carries
  the exception with no message and `ModelStateProblemExtensions` writes the sentence instead.
  Malformed JSON — a `null` inside an array included — is therefore a `400` describing the request,
  not a `500` describing the server.
- **Every ProblemDetails carries the same three members.** `ProblemDetailsNormaliser` is the single
  funnel, so a `400` the framework produced before any of this repository's code ran — a body that
  is not JSON, a failed `:guid` route constraint, an unacceptable media type — still fills in
  `code`, `traceId` and `type`, and never overwrites a value a producer already set. A client that
  always reads `code` does not break on exactly the inputs most likely to be malformed.
- **Another user's resource is a `404` and never a `403`** — a `403` would confirm the resource
  exists. Two mechanisms reach that answer, and which one applies depends on whether an aggregate is
  loaded. A read that projects rows filters on the owner **inside the query**, so the row never
  leaves the database. A command that has to load the aggregate first cannot: it fetches by id and
  compares the owner afterwards, in one place per feature —
  `Src/Application/AppTemplate.Application/Features/Files/Services/StoredFileService.cs` is the
  Files one — and answers the same `404` either way. The guarantee is the answer, not the mechanism;
  a new command that loads an aggregate must go through that gate rather than assume a query filtered
  for it.
- **Optimistic concurrency** on writes via PostgreSQL's `xmin`, translated to a
  `ConcurrencyConflictException` and a `409`.
- **Conditional requests.** Every read of a `TodoList` or `TodoItem` publishes a strong `ETag`, and
  every write honours `If-Match` — a stale, malformed or unrecognised version is refused with `412`.
  `If-Match: *` and no `If-Match` header at all are both accepted while `Concurrency:IfMatch` is
  `Optional`; see the known gap
  below for what stays your responsibility.
- **Request logging never touches the `Authorization` header, cookies, the query string or the body.**
  `Common/Observability/RequestLoggingMiddleware.cs` logs a fixed field list, so a credential cannot
  arrive in a log by accident. The auth endpoints
  carry passwords and refresh tokens in their bodies, which is why body logging is not merely
  disabled but absent.

### Signing in through an external provider

The client runs the OAuth/PKCE flow itself and posts the provider's `id_token`; the API verifies it
and mints **its own** access/refresh pair. No cookie, no browser redirect, and the token model is
untouched — which is what makes the same endpoint work for a SPA, a mobile app and a desktop client.

- **Verification is complete or it is a refusal.** Signature against the provider's JWKS, `iss`,
  `aud` (the configured client id), `exp` and `nbf`. There is no configuration that turns any of
  those off. Key sets are cached and refreshed, so a provider's key rotation does not lock everyone
  out and does not make the provider a dependency on every sign-in.
- **A link is keyed on `(provider, subject)`, never on the email address.** Apple returns the address
  only at the first authorisation, so resolving by address would break the second Apple sign-in —
  silently, and only in production.
- **Automatic linking requires both sides to have proved the address.** The provider must assert
  `email_verified`, *and* an existing local account must already have `EmailConfirmed`. A local
  account whose address was never confirmed is an unproven claim on it, so the link is **refused**
  rather than made — otherwise registering `victim@example.com` and never confirming it would hand
  the attacker the account the victim later creates by signing in with Google.
- **An account provisioned this way has no password and a confirmed address.** The provider vouched
  for it; asking the user to confirm an address they just proved would be theatre.
- **The second factor is not bypassed.** External sign-in runs the same tail as local sign-in: an
  account with two-factor enabled gets a challenge, not a token pair. Without this, linking a Google
  identity would have been the way around 2FA.
- **A refusal says as little as the local one does.** Invalid token, unknown provider, unverified
  address, unconfirmed local account and locked-out account are deliberately hard to tell apart from
  outside, on the same reasoning as `/auth/login`.
- **No client secret is involved.** Verifying an `id_token` is asymmetric cryptography; the template
  holds no secret for any provider, and a deployment configuring one has taken the wrong flow.

### File storage

A file upload is the most dangerous surface an API can grow, so what is decided and what is not is
written out rather than left to be inferred. Everything below is shipped behaviour except where it
says otherwise.

- **The API never carries a byte, in either direction.** Registering a file returns a *signed upload
  grant* and the client writes to the object store directly; reading returns a *signed download
  grant* and the controller answers `302`. This is not an optimisation. `RequestLimits:MaxRequestBodyBytes`
  is 65 536 with a validated ceiling of 30 MiB, and `IdempotencyFilter` buffers and SHA-256s the
  entire body of every `POST` — routing 200 MiB through either would be untenable.
- **A signed URL is a bearer right.** Anyone holding it can read the object for as long as it lives,
  so `Storage:MaxGrantLifetime` bounds it and the decision about *who* may have one belongs to the
  use case, which compares `OwnerId` against `ICurrentUser` before issuing anything. That is the
  IDOR defence, and it is in the application layer where the other two features put it, not in the
  URL.
- **The bucket must never allow anonymous reads.** `docker-compose.yml`'s bucket bootstrap sets
  `anonymous none` explicitly. A readable bucket makes every grant in this template decoration.
  The MinIO service in that file publishes both its ports on loopback — `127.0.0.1:9000` for the
  S3 API and `127.0.0.1:9001` for the console — like every other published port in the stack, so a
  development object store holding whatever was uploaded to it is not reachable from the network
  the developer's machine happens to be on.
- **A file name is a label, never a path.** `StoredFileName` refuses separators, control characters
  and reserved device names, and the object key is generated independently of it — so nothing a
  client sends can steer where bytes land. The name never reaches a filesystem at all.
- **The declared media type is a claim, and it is now checked against the bytes.** A deposited file
  is inspected before it becomes readable: the leading bytes are matched against the declared type,
  and the content is scanned. A file whose real type disagrees with its declaration is
  **quarantined**, not served. **Markup is refused outright, whatever it was declared as** — an SVG
  is a script container, and there is no version of "serve it inline" that is safe. Two checks in
  `Src/Application/AppTemplate.Application/Features/Files/Policies/MediaTypeSignatures.cs` do
  that, and the second is the one that cannot be walked past: the first searches the inspected
  prefix for `<svg`, `<html`, `<script` and a doctype, which an author can evade by padding with a
  kibibyte of XML comment, and the second refuses any document whose *first* meaningful byte is
  `<` — offset zero being the one thing padding cannot move, since everything XML allows before a
  root element is either whitespace or itself a tag. A byte-order mark does not hide it and neither
  does UTF-16.
  **This refuses honest XML too**, deliberately: nothing here sanitises markup, and the download
  path hands out a URL to an origin this application does not control. A project that has to accept
  XML changes `MediaTypeSignatures` and owes a sanitiser and a serving path that cannot execute what
  it stores.
- **An upload grant authorises one body, not one length.** The SHA-256 the client declared at
  registration is bound into the signature, so the store refuses content whose digest disagrees. That
  is what stops the grant being replayed to swap content *after* the file has been inspected and
  released — a grant lives `Storage:MaxGrantLifetime` (thirty minutes) while inspection runs every
  minute, so for most of its life the file it belongs to has already been examined. Measured before
  the digest was bound: a second deposit of different bytes of the same length answered `200`.
- **Inspection happens in the worker, not in the request.** Scanning 200 MiB inside an HTTP request
  is the same CPU-denial problem as resizing an image there, which this template refuses by name.
  The consequence is a state a client can observe: a confirmed upload is `deposited`, not
  `available`, until the loop has decided — and `FileWorker:InspectDepositedFilesInterval` is
  therefore a user-visible latency rather than a cost.
- **No verdict is not a pass.** A scanner that cannot be reached leaves the file `deposited` and
  retries; it never releases it. Fail-open would make an outage a way through, which is the one
  failure mode that is worse than the outage.
- **`Quarantined` is a persisted, terminal state, and it costs no query a predicate.** The only
  place that hands out bytes guards with an allow-list — `State != Available` refuses — so a new
  state is refused the day it is added rather than the day someone remembers to extend a predicate.
  That asymmetry is what makes this different from the deleted flag `NoPersistenceRecord_CarriesADeletedFlag`
  forbids. Read projections deliberately do **not** filter it out: an owner has to be able to see
  that their file was refused.
- **The refusal does not say what tripped it.** Telling a depositor which detector matched hands
  them a test bench for tuning a payload until it passes. The detail goes to the operator's log
  instead.
- **Decompression bombs are still not addressed**, because nothing here decompresses anything —
  inspection reads leading bytes and streams to the scanner, and neither expands input. Any
  derivative pipeline added later must bound output size and time **before** it reads an archive or
  an image, not after.
- **Storage exhaustion is bounded per owner**, by a policy in the application layer rather than by
  the store — a registration mints a signed upload grant, so an unbounded one is a free way to fill
  someone else's bucket. Pending registrations and available bytes are budgeted separately, because
  an unconfirmed registration costs a reserved key and nothing more.
- **Long-lived storage credentials are avoided by default.** `Storage:AccessKeyId` and
  `Storage:SecretAccessKey` are empty in every tracked file, and empty is the intended production
  shape: the SDK's own chain resolves an instance role, and nothing holds a key pair. The validator
  refuses exactly one of the two, since half a pair means signing anonymously.

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
- **Migrations are not applied at startup outside Development.**
- **The access-token signing key reaches one host, not both.** `AppTemplate.Worker` composes the
  identity module — it has to, see `docs/CONFIGURATION.md` — and `JwtOptionsValidator` therefore
  demands a `Jwt:Key` of it at startup. That host signs nothing and verifies nothing: `AccessTokenIssuer`
  is the only thing that uses the key, no background loop resolves it, and bearer validation needs an
  inbound request this process does not have. So it is given a self-describing placeholder from
  `deploy/kubernetes/configmap-worker.yaml` and `docker-compose.yml`, and only `api-deployment.yaml`
  references the real value in `deploy/kubernetes/secret.example.yaml`. Both hosts still share
  `Jwt:Issuer` and `Jwt:Audience`, which are the two values they genuinely have to agree about.
  Until this change the worker mounted the API's signing key, which made a container whose whole job
  is sending reminder mail and deleting expired rows one compromise away from minting an access
  token for any user in the system.

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
   in source control. Same for `ConnectionStrings:Default` and the SMTP credentials. Give it to
   `AppTemplate.Api` and to nothing else — the worker needs the *section*, not the *key*.
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

- **There is no self-service account deletion, and the administrative one leaves business data
  behind.** Deletion is `DELETE /api/v1/auth/accounts/{userId}`, behind the `Administrator` policy,
  and it refuses the caller's own account. A user cannot erase themselves at all — a deployment
  under an erasure obligation has to add that endpoint, and it should carry a password re-proof for
  the same reason changing an address does. What deletion does remove is the account row and
  everything ASP.NET Identity owns with it — grants, claims, logins, tokens — and nothing else. A user's to-do lists and reminders carry an `OwnerId` that is a plain `Guid` with no
  foreign key to the account, by the same rule that keeps aggregates from referencing each other
  across features, so nothing cascades to them. They become unreachable, because every read filters
  by owner, and they stay in the database indefinitely.
  That is storage growth and, where the data is personal, an erasure obligation the template does
  not discharge. Closing it is a deployment's decision about *what deletion means* — refuse while
  data remains, reassign it, or remove it — and the shape to copy is the scheduled purge the
  maintenance endpoints already use, not a foreign key added across a feature boundary.

- **Sending mail inside the request leaks which addresses exist, by timing.** Forgot-password,
  resend-confirmation and change-email all answer identically and log nothing, but each awaits an
  SMTP round trip — connection, STARTTLS, AUTH, DATA — **only on the branch where the account
  exists**. The difference is orders of magnitude, needs no averaging to read, and grows further
  when the relay is slow or down. Change-email is authenticated, so any registered user can test an
  arbitrary address this way. The "including in timing" claim above holds for `/login`, which was
  written for it, and not for these three.
  Closing it means taking delivery off the request: queue the message durably and answer on both
  branches at once. A detached task is not the fix — it drops the mail on the next deployment — and
  neither is a fixed delay, which would have to exceed the relay's own variance to hide anything.
- **The background work has no high availability, and no number of replicas fixes that today.**
  `deploy/kubernetes/worker-deployment.yaml` fixes `replicas: 1` and argues only against *two*.
  It says nothing about **zero**, which is the interesting number: with that pod down, no reminder
  is rung, no expired refresh-token grant is deleted and no idempotency key is reclaimed, and
  nothing alerts on any of it — the host serves no traffic, so it has no readiness probe. Each of
  the three loops does now emit a heartbeat, and it is what a deployment should alert on going flat:
  `apptemplate.worker.{maintenance,files,reminders}.iterations`, counted once per pass and never
  gated on how much the pass found, so a healthy quiet system still produces samples. Alert on the
  pod's restart count alongside them.
  Do **not** alert on the absence of `apptemplate.reminders.missed_cancellations`, which this
  paragraph used to advise: that counter increments only when a cancellation was missed, so silence
  is its healthy state and it cannot tell a working loop from a dead one.
  Raising the count is now **safe**, and it was not before. The manifest's own comment reasoned
  about the two purges — idempotent deletes over an already-covered range, so a second replica
  wastes a connection and nothing more — and was silent about the loop where it mattered.
  `FireDueRemindersUseCase` claims a reminder with `Reminder.TryClaim` **in memory**, notifies, and
  commits the whole batch once at the end, so two replicas ticking in the same second both read
  `ClaimedAt` as null, both took the claim, and both sent the mail; only then did one of them lose
  on `xmin` and roll back. The claim defends against a host that died mid-attempt, which is what it
  was written for, and not against a concurrent pass.
  The pass now runs under `ILeaderLease`, whose adapter is a PostgreSQL session-level advisory lock
  on its own unpooled connection: exactly one host runs a pass, and losing that process closes the
  session and releases the lock rather than stranding a lease until a timer says otherwise. The
  guard is in the use case rather than in the `BackgroundService`, so it also covers any future
  caller — `MaintenanceController` is the standing proof that one turns up.
  **It is not a fencing token.** Leadership can be lost mid-run without the work being told, so
  delivery stays at-least-once and anything else put under a lease has to survive a second host
  starting it. What the lease removes is the systematic duplication of every single pass.
  Still open, and the reason this entry stays here: nothing yet alerts when the loops stop.
- **Nothing bounds distributed credential stuffing.** Lockout is per account and rate limiting is
  per client address, so one password tried against a hundred thousand accounts from a thousand
  addresses trips neither. Closing it needs something neither of those two mechanisms is: a global
  bound on `/login` failures, a compromised-password check, or an alert on the rate.
- **Lockout is itself a denial of service.** Five failures lock an account for fifteen minutes, so
  roughly twenty requests an hour keep a chosen account shut out indefinitely. A growing delay, or
  notifying the account owner, are the usual answers; neither is here.
- **One JWT signing key, with no rotation and no `kid`.** Replacing it invalidates every token in
  circulation at once, because there is no overlap window in which two keys are accepted. A `jti` is
  issued but nothing consumes it, so an individual token cannot be revoked either.
- **A residual timing difference on the login path.** The two branches derive a key either way, so
  the expensive part is constant, but a wrong password writes an access-failure count where an
  unknown address does not. It is far smaller than the hash and always the same sign, so it is
  extractable by averaging. Making it nil would mean writing for a user that does not exist.
- **Nothing proves the process actually drains on SIGTERM.** The readiness flip is tested, and the
  Kubernetes manifests carry the `preStop` and grace period it needs, but that Kestrel finishes
  in-flight requests and that `ShutdownTimeout` is honoured are asserted nowhere — doing so means
  running the binary and signalling it, which is a different kind of test than any here.
- **`If-Match` is optional by default.** Every read publishes a strong `ETag` and every write
  honours `If-Match`, refusing a stale or unrecognised version with `412`, but a request that sends
  no `If-Match` at all is still accepted unless `Concurrency:IfMatch` is set to `Required` — see
  `Concurrency:IfMatch`. Until you set it, a slow user's form submission can still overwrite a change made
  after it was rendered without anything detecting it.
- **Domain-event delivery is best-effort, with no outbox.** Consumers are now isolated from one
  another: one throwing is logged with its event and consumer type, and the remaining consumers of
  that same event still run. What remains open is narrower but real — the throwing consumer's own
  side effect is lost and never retried, and a process that dies between the commit and the dispatch
  loop loses every consumer for that save, because nothing durable recorded that the event was
  raised. Closing that needs an outbox, plus a dispatcher, a dead-letter path and idempotent
  consumers, and this template refuses to ship half of one: a dispatcher with no dead-letter queue
  and no alert on lag is worse than an acknowledged gap, because it looks solved.
  The reminder feature shows the alternative, and it is the pattern to copy: the effect re-reads
  the state it depends on at the moment it acts, so a lost cancellation delays a reminder's
  retirement instead of firing it wrongly, and the reminder worker counts every such divergence —
  a non-zero `apptemplate.reminders.missed_cancellations` is the number of events that went missing. A
  consumer whose effect *cannot* be re-derived, because nothing re-reads the state later — mail,
  money, a call to a third party — still needs an outbox before you rely on it.
  No consumer is the only thing keeping a rule true: every effect re-derives its precondition when
  it runs, so a lost event leaves the system stale rather than wrong.
- **Idempotency keys expire only where the worker runs.** `Idempotency:Retention` stamps each row's
  `ExpiresAt`; it does not delete anything. `AppTemplate.Worker`'s maintenance loop calls the purge
  on its own schedule, and the same operation is exposed as
  `DELETE /api/v1/maintenance/idempotency-keys/expired` behind the `Administrator` policy for a
  deployment that runs no worker. Deploy neither and a completed key stays replayable past its
  retention window while the table grows without bound.
- **The auth wire format has two owners.** `ConfigureJwtBearerOptions` in the Identity module builds
  its own `ProblemDetails` and owns the `auth.required` / `auth.forbidden` codes, so a change to how
  failures look must be made in two places.
- **Cancellation is not propagated through Identity.** `UserManager` and `SignInManager` accept no
  `CancellationToken`, so an abandoned request still runs its user-store I/O to completion.
- **No audit log of security-relevant events.** Logins, lockouts, token-family revocations and
  password changes are traced but not recorded in a queryable, tamper-evident store. This stays open
  deliberately: a table this application can `UPDATE` and `DELETE`
  through the same connection as business data would look like an audit trail without being one, and
  what closing it properly requires.
