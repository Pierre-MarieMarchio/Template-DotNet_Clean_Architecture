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

<!--#if (localAuth)-->
### Authentication and session handling
- **Default-deny authorisation.** `Program.cs` sets `AuthorizationOptions.FallbackPolicy` to one
  requiring an authenticated user, so an endpoint is protected unless it carries an explicit
  `[AllowAnonymous]`. Forgetting `[Authorize]` does not publish an endpoint. The policy also
  applies where no endpoint matched, so an unknown route answers `401` to an anonymous caller.
- **Opaque, rotating refresh tokens**, never JWTs. Only a hash is stored. Presenting a token always
  consumes it, and replaying one that was already consumed **revokes every session of that user**.
  `RefreshTokenTable` does the consumption as a single conditional `UPDATE` — zero affected
  rows *is* the replay signal — so two simultaneous presentations cannot both succeed. The
  consumption commits in one transaction with its successor, so a failed save leaves the token
  usable rather than spent, and a retry is never mistaken for a replay. A token of a
  session that was ended — signed out, revoked by its owner, or killed by a credential change — is
  refused without that alarm: it is what a client still holding it sends next, and the revocation
  commits before or during a rotation always wins.
- **Sessions an owner can see and end.** One sign-in is one session: a refresh-token family, with
  its creation time, its last use and the client's `User-Agent` as of that use. The access token
  carries a `sid` claim naming the session it was issued in. `GET /auth/sessions` lists the caller's
  own live sessions — marking the one the request comes from — and
  `DELETE /auth/sessions/{id}` ends one; a session belonging to somebody else answers `404`,
  exactly as an unknown id does, so the route cannot be used to learn which session ids exist.
  Deliberately stored about a client: its `User-Agent` string, truncated. Deliberately not stored:
  the address the request came from, which is personal data with no owner-facing use.
- **A credential change ends every other session.** A password change from a signed-in session,
  arming or disarming the second factor, and removing a passkey revoke every session of the account
  but the one the change was made from — that session proved the new posture, and its access token still dies with the
  rotated security stamp; its refresh token mints the replacement. A password reset through the
  mailed link, which is not made from any session, still revokes them all.
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
- **Resistance to account enumeration on login, confirm-email and resend-confirmation.** Login
  does not distinguish an unknown user from a wrong
  password, *including in timing* — `AccountsService` runs a deliberate decoy-hash path built
  from the configured hasher, so both branches cost the same. Confirm-email answers identically for
  an unknown address and a bad token. Resend-confirmation answers identically whether or not the
  address exists. Registration is not covered: see "Known gaps".
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
  Enabling or disabling the second factor rotates the security stamp and ends every session of the
  account but the one the change is made from, exactly like a password change — **and requires the
  current password on both sides of that symmetry**: arming the second factor is a
  security-posture change that costs the caller every other session, exactly as disarming it does,
  so a stolen access token alone can do neither.
- **An administrative escape hatch for a second factor nobody can prove possession of any more**: an
  account that has lost the authenticator app *and* its recovery codes cannot complete `/login` at
  all, and had no recourse short of deleting the account outright. An administrator can strip that
  account's second factor directly, on the strength of the `Administrator` policy rather than that
  account's own credential — and is refused with a 403 against their own account, so a stolen
  administrator session cannot use this route to do to itself what the symmetry above exists to
  prevent a stolen session doing.
- **Passkeys (WebAuthn)**, built entirely on ASP.NET Core Identity's own support for .NET 10 — no
  hand-rolled CBOR, no third-party FIDO2 library. The ceremonies are over
  `IPasskeyHandler`, not `SignInManager`'s passkey methods: those park the ceremony state in a
  cookie, and this API sets none. The state travels to the client as an opaque string sealed by
  ASP.NET Core data protection — a time-limited protector, under a purpose per ceremony kind, the
  registration one naming its account — so the client can neither read nor rewrite it, nor carry
  a registration's into a sign-in. It expires after `Passkey:CeremonyLifetime` (five minutes) and
  is single-use: its first presentation records a hash of it in `identity.SpentPasskeyCeremonies`,
  one `INSERT` keyed on that hash and made before the ceremony is verified, so a replay — even
  from a synced passkey whose signature counter never moves — and the loser of two simultaneous
  presentations are refused, and a ceremony that fails still spends its state. Every such refusal
  answers as a ceremony that does not verify. Origin
  validation is the framework's own: the client data's origin must equal the request's `Origin`
  header, and the relying-party id comes from `Passkey:ServerDomain` when set, else the request's
  host. User verification is `required` and not configurable, which is what lets a verified passkey
  stand in for the whole of a sign-in — an armed second factor is bypassed exactly as ASP.NET Core
  Identity's own `PasskeySignInAsync` bypasses it, and the factor still governs the ways in that
  prove less. Signature counters are persisted **before** the sign-in's tokens are minted, so the
  replay window closes first; a counter that did not advance (the clone signal) refuses the
  ceremony. Registering a passkey demands re-authentication — the current password, or a recently
  issued access token for an account that offers nothing its holder could present — because a
  passkey is a whole way in, not a second step on one; listing and removing one stay within the
  caller's own account, with the same 404 for somebody else's credential as for an unknown id.
  Removing one is a credential change: it rotates the security stamp — Identity's own removal does
  not, so the adapter does — and ends every other session, as a password change does, so a session
  opened with the withdrawn passkey does not outlive it. Registering one ends no session.
  The credential rows live in the identity schema (`identity.UserPasskeys`), deleted with the
  account by cascade, and their payload — public key, counters, attestation — is one `jsonb`
  column the store reads whole and never queries field by field.

<!--#endif-->

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
<!--#if (localAuth)-->
- Rate limiting per client address: 10 requests/minute on the authentication endpoints, 300/minute
  globally, answering `429` with `Retry-After` and a ProblemDetails body. The two numbers are
  `RateLimiting:AuthenticationPermitLimit` and `RateLimiting:GlobalPermitLimit`, over the
  one-minute `RateLimiting:Window`; those are the defaults.
<!--#else
- Rate limiting per client address: 300/minute globally, answering `429` with `Retry-After` and a
  ProblemDetails body. The number is `RateLimiting:GlobalPermitLimit`, over the one-minute
  `RateLimiting:Window`; that is the default.
<!--#endif-->
- **This is only correct if `ReverseProxy` is configured — see the deployment obligations.**
- Bounded aggregates: a maximum number of items per list and tags per item, so one request cannot
  make the server do unbounded work.
- **An oversized body is refused before it is buffered, and the refusal is logged.** The limit is
  `RequestLimits:MaxRequestBodyBytes`, except on the file upload endpoint described under File
  storage.
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
<!--#if (files)-->
- **Another user's resource is a `404` and never a `403`** — a `403` would confirm the resource
  exists. Two mechanisms reach that answer, and which one applies depends on whether an aggregate is
  loaded. A read that projects rows filters on the owner **inside the query**, so the row never
  leaves the database. A command that has to load the aggregate first cannot: it fetches by id and
  compares the owner afterwards, in one place per feature —
  `Src/Application/AppTemplate.Application/Features/Files/Services/StoredFileService.cs` is the
  Files one — and answers the same `404` either way. The guarantee is the answer, not the mechanism;
  a new command that loads an aggregate must go through that gate rather than assume a query filtered
  for it.
<!--#else
- **Another user's resource is a `404` and never a `403`** — a `403` would confirm the resource
  exists. Two mechanisms reach that answer, and which one applies depends on whether an aggregate is
  loaded. A read that projects rows filters on the owner **inside the query**, so the row never
  leaves the database. A command that has to load the aggregate first cannot: it fetches by id and
  compares the owner afterwards, in one place per feature, and answers the same `404` either way. The guarantee is the answer, not the mechanism;
  a new command that loads an aggregate must go through that gate rather than assume a query filtered
  for it.
#endif-->
- **Optimistic concurrency** on writes via PostgreSQL's `xmin`, translated to a
  `ConcurrencyConflictException` and a `409`.
- **Conditional requests.** Every read of a single versioned resource publishes a strong `ETag`, and
  every write honours `If-Match` — a stale, malformed or unrecognised version is refused with `412`.
  `If-Match: *` and no `If-Match` header at all are both accepted while `Concurrency:IfMatch` is
  `Optional`; see the known gap
  below for what stays your responsibility.
- **Request logging never touches the `Authorization` header, cookies, the query string or the body.**
  `Common/Observability/RequestLoggingMiddleware.cs` logs a fixed field list, so a credential cannot
  arrive in a log by accident.
<!--#if (localAuth)-->
  The auth endpoints
  carry passwords and refresh tokens in their bodies, which is why body logging is not merely
  disabled but absent.
<!--#endif-->

<!--#if (localAuth)-->
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
<!--#endif-->

<!--#if (externalAuth)-->
### External identity provider

A host can authenticate callers with access tokens from an external OpenID Connect provider instead
of the local accounts: `AddExternalAuthentication` in `Api/Features/ExternalIdentity/Composition/`,
configured by [`Authentication:External`](docs/CONFIGURATION.md#authenticationexternal).

- **Validation is the framework's, with nothing switched off.** Signature against the authority's
  published keys, `iss` from its discovery document, `aud`, `exp` and `nbf`, a 30-second skew.
  Claim types are left as the provider wrote them, so `sub` never arrives as `NameIdentifier`.
- **The provider's subject is opaque and never becomes an owner id.** The pair (issuer, subject) is
  mapped to an internal `UserId` in `external_identity.ExternalIdentities`, and that id is what
  `ICurrentUser` returns and every business row carries — even when the provider's subjects happen
  to be GUIDs. An email address is never the key: it is kept, refreshed when the provider sends a
  new one, and used only to reach the owner. It is not kept when the token's `email_verified` is
  anything but `true`; a token without that claim keeps it, so a provider that issues unverified
  addresses without the claim should have `EmailClaim` mapped off.
- **The mapping is created on the caller's first request, exactly once.** A unique index on
  (issuer, subject) decides between two concurrent first requests: the loser's insert is refused,
  it reads the winner's row, and both answer the same owner. Each write commits on its own, outside
  the request's unit of work, and a retried insert that had in fact committed is answered from its
  own row. The owner id of a mapping is never rewritten.
- **Fails closed.** A token that does not validate, or that validates but names no issuer or no
  subject, is a 401 with the usual `auth.required` body. A store failure fails the request. There is
  no anonymous fallback and no shared placeholder owner.
- **Roles come from one configured source.** Either a claim path of the token (`roles`,
  `realm_access.roles`, a namespaced claim) or the local `external_identity.RoleAssignments` table;
  never both, and a provider-sent role claim is dropped when the source is local. A missing claim is
  no role. The `Administrator` policy requires the configured role. Local assignments have no API:
  they are written by SQL or a seed.
- **Keeping existing owners across a change of provider.** Before the switch, insert one mapping row
  per owner naming the new provider's issuer and subject and the owner's *existing* id:
  `INSERT INTO external_identity."ExternalIdentities" ("Id", "Issuer", "Subject", "UserId", "Email",
  "DisplayName", "CreatedAt", "LastSeenAt") VALUES (gen_random_uuid(), '<issuer>', '<subject>',
  '<existing owner id>', NULL, NULL, now(), now());`. An owner may have one row per provider; all name
  the same id. The subjects come from the provider (an export, or its admin API), matched on whatever
  the migration trusts — that matching is the one step where an address may be used, by a person.
<!--#if (reminders)-->
- **Reminders still reach their owner.** A background pass has no token, so the recipient is read
  from the address and name the provider last sent, kept on the mapping. An owner whose provider never
  sent an address is skipped.
<!--#endif-->

<!--#endif-->
<!--#if (files)-->
### File storage

A file upload is the most dangerous surface an API can grow, so what is decided and what is not is
written out rather than left to be inferred. Everything below is shipped behaviour except where it
says otherwise.

- **Bytes reach the API on one endpoint only, and only up to a configured ceiling.** A direct
  upload registers the file, receives a *signed upload grant* and writes to the object store; no
  byte passes through the API, and large files belong there. `POST /api/v1/files/uploads` is the
  one endpoint whose body carries a file: `multipart/form-data`, bounded by
  `Files:ApiUploadMaxBytes` (10 MiB by default, 30 MiB at most) plus 16 KiB of framing, a limit
  raised for that endpoint alone while every other body keeps `RequestLimits:MaxRequestBodyBytes`
  (65 536). A body over it is `413` before it is read when it declares its length, and is cut off
  by Kestrel's per-request limit when it does not. It is not `[Idempotent]`, so
  `IdempotencyFilter` never buffers or hashes it; the form buffers the file to a temporary file
  above 64 KiB rather than in memory. The request never inspects the content: the file is stored
  and left `Deposited`, and the background inspection decides, exactly as for a direct upload. A
  category's `UploadModes` decides which of the two paths it accepts. A store that cannot sign
  (`Files:Storage:Provider` = `FileSystem`) refuses direct-upload registration with `409`
  `storedFile.directUploadUnavailable`, so with it bytes arrive through this endpoint only.
- **Downloads are redirected when the store can sign, and streamed when it cannot.** With S3, reading
  returns a *signed download grant* and the controller answers `302`; no byte passes through the API.
  With the `FileSystem` provider the API carries the bytes itself: `200`, after the same ownership
  and state checks, as an attachment under the file's name, with the declared media type,
  `X-Content-Type-Options: nosniff` and `Cache-Control: no-store`. The file system store maps an
  object key to a path under its root and refuses any key that would resolve outside it; a
  client-supplied name never reaches the disk.
- **A signed URL is a bearer right.** Anyone holding it can read the object for as long as it lives,
  so `Files:Storage:S3:MaxGrantLifetime` bounds it and the decision about *who* may have one belongs to the
  use case, which compares `OwnerId` against `ICurrentUser` before issuing anything. That is the
  IDOR defence, and it is in the application layer where the other two features put it, not in the
  URL.
- **`Authenticated` visibility opens the content, and only the content, to every signed-in user.**
  A category declared `Visibility: Authenticated` (the shipped `image` category is one) lets any
  authenticated caller download an *available* file of it by its id — the shape of an avatar or a
  shared attachment. The id then works as a capability: it is unguessable (UUIDv7 carries 74 random
  bits) but whoever learns it can read the file, so do not use this visibility for anything
  confidential. Metadata, tags, confirmation and deletion stay owner-only, a file that is not
  available answers `404` to a non-owner exactly like a missing one, and there is no anonymous
  access. A file whose category was removed from configuration is treated as `Private`.
- **The bucket must never allow anonymous reads.** A readable bucket makes every grant in this
  template decoration. `docker-compose.yml`'s `objectstore` service (SeaweedFS) is given exactly one
  S3 identity, and an identity existing is what makes its gateway answer every unsigned request —
  read, list or write — with `403 AccessDenied`; with none configured it would serve anyone, so
  never remove the key pair to "simplify" the stack. It publishes one port, the S3 API, on loopback
  (`127.0.0.1:9000`) like every other published port in the stack, so a development object store
  holding whatever was uploaded to it is not reachable from the network the developer's machine
  happens to be on. Its other components (master, volume server, filer) listen inside the compose
  network only, unauthenticated, as SeaweedFS ships them — acceptable for a development store whose
  only neighbours are this stack's own containers, and one more reason it is not a production one.
- **A file name is a label, never a path.** `StoredFileName` refuses separators, control characters
  and reserved device names, and the object key is generated independently of it — so nothing a
  client sends can steer where bytes land. The name never reaches a filesystem at all.
- **The declared media type is a claim, and it is checked against the bytes.** A deposited file
  is inspected before it becomes readable: the leading bytes are matched against the declared type
  when the file's category has `VerifySignature` (the default), and the content is scanned when the
  category has `Scan` — which start-up allows only with `Files:Scanning:Enabled`. A file whose real
  type disagrees with its declaration is **quarantined**, not served. Turning `VerifySignature` off
  releases a file whatever its leading bytes say, so the declared type a client later receives is an
  unchecked claim; turning `Scan` off (the shipped state, scanning being disabled) releases content
  no antivirus has seen. A file whose category was removed from configuration is inspected with both
  checks, the scan running when scanning is enabled. **Markup is refused outright, whatever it was declared as** — an SVG
  is a script container, and there is no version of "serve it inline" that is safe. Two checks in
  `Src/Application/AppTemplate.Application/Features/Files/Model/Policies/MediaTypeSignatures.cs` do
  that, and the second is the one that cannot be walked past: the first searches the inspected
  prefix for `<svg`, `<html`, `<script` and a doctype, which an author can evade by padding with a
  kibibyte of XML comment, and the second refuses any document whose *first* meaningful byte is
  `<` — offset zero being the one thing padding cannot move, since everything XML allows before a
  root element is either whitespace or itself a tag. A byte-order mark does not hide it and neither
  does UTF-16.
  **This applies in every category, whatever its `VerifySignature`**, and it refuses honest XML too,
  deliberately: nothing here sanitises markup, and the download
  path hands out a URL to an origin this application does not control. A project that has to accept
  XML changes `MediaTypeSignatures` and owes a sanitiser and a serving path that cannot execute what
  it stores.
- **An upload grant authorises one body, not one length.** The SHA-256 the client declared at
  registration is bound into the signature, so the store refuses content whose digest disagrees. That
  is what stops the grant being replayed to swap content *after* the file has been inspected and
  released — a grant lives `Files:Storage:S3:MaxGrantLifetime` (thirty minutes) while inspection runs every
  minute, so for most of its life the file it belongs to has already been examined. Measured before
  the digest was bound: a second deposit of different bytes of the same length answered `200`.
- **Inspection happens in a background job, not in the request.** Scanning 200 MiB inside an HTTP request
  is the same CPU-denial problem as resizing an image there, which this template refuses by name.
  The consequence is a state a client can observe: a confirmed upload is `deposited`, not
  `available`, until the loop has decided — and `Jobs:Files:InspectDepositedFilesInterval` is
  therefore a user-visible latency rather than a cost.
- **No verdict is not a pass.** A scanner that cannot be reached leaves the file `deposited` and
  retries — one minute later, doubling, at most an hour — behind the files that have waited longer;
  it never releases it and never refuses it. Fail-open would make an outage a way through, which is
  the one failure mode that is worse than the outage.
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
- **Storage exhaustion is bounded per owner and per category**, by a policy in the application layer
  rather than by the store — a registration mints a signed upload grant, so an unbounded one is a
  free way to fill someone else's bucket. The bounds are the category's `Quota`. In `Strict` mode
  (the shipped default) the usage read, the insert and the commit run under a PostgreSQL advisory
  lock on (owner, category), so the bound holds under concurrency across every host sharing the
  database, at the cost of one extra connection per registration in flight and one registration at
  a time per owner and category; a wait over 10 seconds is refused (`storedFile.quotaBusy`). In
  `Indicative` mode there is no lock, and a burst of N concurrent registrations can overshoot by at
  most N − 1 files and their declared bytes. Deletion, purge and inspection never take the lock.
  Pending registrations and available bytes are budgeted separately, because an unconfirmed
  registration costs a reserved key and nothing more.
- **Long-lived storage credentials are avoided by default.** `Files:Storage:S3:AccessKeyId` and
  `Files:Storage:S3:SecretAccessKey` are empty in every tracked file, and empty is the intended production
  shape: the SDK's own chain resolves an instance role, and nothing holds a key pair. The validator
  refuses exactly one of the two, since half a pair means signing anonymously.

<!--#endif-->
### Supply chain and configuration
- `NuGetAudit` at the `low` level with `NuGetAuditMode=all`, so **a known advisory in a direct or
  transitive package fails the build**, not just a report. CI checks it again independently.
- An advisory reaching the tree through a package nothing references directly is answered with a
  **transitive pin** in `Directory.Packages.props` under a `Security pins` label, which
  `CentralPackageTransitivePinningEnabled` makes bind. Removing a load-bearing pin fails
  `dotnet restore` outright rather than passing quietly.
- Every GitHub Action is pinned to a commit SHA, not a mutable tag.
- **Every container image is pinned by digest**, written `name:tag@sha256:…`: the services in
  `docker-compose.yml`, the Dockerfiles' bases and their `# syntax=` frontend, the Testcontainers
  fixtures, and the images the Docker actions pull for the workflows (binfmt, the BuildKit daemon,
  the SBOM scanner). A tag can be re-pointed by whoever controls it on the registry; a digest
  cannot. `ContainerImagePinningTests` holds the first three.
  What this still trusts is the publisher at the moment the digest was taken — Microsoft, the
  Docker official images, and each project's own image — and nothing that happens to its tag after.
- Dependabot watches NuGet, Actions, the Dockerfiles and `docker-compose.yml`, and moves a tag and
  its digest together. Note that neither
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
<!--#if (localAuth)-->
- **The jobs share the API's process, and so its signing key.** The periodic jobs run inside
  `AppTemplate.Api`, which signs and verifies every token, so the process that sends mail and
  deletes expired rows holds the real `Jwt:Key`. That is a deliberate trade for one process and one
  image, and it gives up a separation: a separate worker needs the `Jwt`
  section only to start — no background loop resolves `AccessTokenIssuer` — and can be given a
  self-describing placeholder, so a compromised background container could not mint a token. A
  deployment that wants that property runs the jobs in a process of its own, sets
  `Jobs:Enabled=false` on the API, and gives that process a placeholder `Jwt:Key` with the
  same `Jwt:Issuer` and `Jwt:Audience`; nothing in the jobs reads the key.
<!--#endif-->

## What a deployment must still do

### Required — the template is insecure without these

1. **Terminate TLS upstream and enforce HTTPS there.** The app serves plain HTTP by design.
2. **Send HSTS from the ingress.** The application will not do it. `max-age`, `includeSubDomains`
   and `preload` are domain-wide commitments that an application cannot know, and a template that
   shipped `preload` would burn a domain for months. Nothing here can detect that you forgot, which
   is exactly why it is first on this list.
3. **Configure the `ReverseProxy` section if anything sits in front of the app.** Until you do,
   `X-Forwarded-For` is ignored and the rate limiter partitions on the proxy's address — so
<!--#if (localAuth)-->
   **every caller in the world shares one 10-request window** and the brute-force protection does not
   work.
<!--#else
   **every caller in the world shares one window** and the limit protects nothing.
#endif-->
   Set `KnownProxies` and/or `KnownNetworks` to the hops you actually run, and `ForwardLimit` to
   their number. Do **not** enable it with empty trust lists to "make it work": ASP.NET Core treats
   two empty lists as *trust every caller*, which lets a client forge its own partition key and
   bypass the limiter just as completely. The options validator refuses to start in that state.
<!--#if (localAuth)-->
4. **Supply the signing secret from a secret manager**, with real entropy, and rotate it. Never
   from a file in source control. Same for `ConnectionStrings:Default` and the SMTP credentials.
   Give `Jwt:Key` to
   `AppTemplate.Api` and to nothing else — a separate job runner, if you extract one, needs the *section*,
   not the *key*.
<!--#elseif (externalAuth)-->
4. **Supply `ConnectionStrings:Default` and the SMTP credentials from a secret manager**, never from
   a file in source control. The signing secret is the provider's, held by the provider; what this
   deployment supplies is `Authentication:External:Authority` and `Authentication:External:Audience`.
<!--#else
4. **Supply `ConnectionStrings:Default` and the SMTP credentials from a secret manager**, never from
   a file in source control.
#endif-->
5. **Set `Cors:AllowedOrigins`** to your real origins, and **narrow `AllowedHosts`** from `*` to the
   hostnames you serve.
6. **Apply migrations as a separate step** — the release workflow builds a self-contained bundle.
   Run it with a **migration-time principal that has DDL rights, and give the application's runtime
   user none**. The app should not be able to alter its own schema.
7. **Grant the database user least privilege**: DML on its own tables, no DDL, no superuser.

### Strongly recommended
<!--#if (localAuth)-->
- Ship the OTLP telemetry somewhere and alert on it — `429` and `401` rates are the signals that
  tell you brute force is happening.
<!--#else
- Ship the OTLP telemetry somewhere and alert on it — the `429` rate is the signal that something is
  hammering the API.
#endif-->
- Set a log retention policy, and re-check redaction after you add any logging of your own. The
  guarantee here covers the middleware this template ships, not code you add.
- Back up the database and rehearse the restore.
- Put an authenticated gateway or network policy in front of `/health/ready` if the fact that your
  database is reachable is information you would rather not publish.
<!--#if (todoLists)-->
- Review the sample `TodoLists` feature out of your deployment if you do not need it.
<!--#endif-->
<!--#if (localAuth)-->
- Set `Passkey:ServerDomain` before the first passkey is registered if the API does not serve the
  domain its clients call — a credential is scoped to the relying-party domain it was created
  under, so discovering the right one after registrations exist invalidates every passkey the
  accounts hold. Pick it once, with the domain your users' browsers see.
<!--#endif-->

## Known gaps

Stated here rather than left to be discovered. None of these is a hypothetical.

<!--#if (accounts)-->
- **Deletion is covered for the features this template ships; a feature you add has to declare its
  own policy.**
<!--#if (localAuth)-->
  A user erases themselves with `POST /api/v1/auth/delete-account`, which demands a
  re-proof first — the current password when the account has one, a two-factor code when the factor
  is armed, and an access token issued less than five minutes ago when the account offers neither —
  so a stolen session alone cannot take the account. An administrator deletes with
  `DELETE /api/v1/auth/accounts/{userId}`, still refused against the caller's own account. Both
  remove the account row and everything ASP.NET Identity owns with it, then publish the
<!--#else
  An administrator deletes the local presence with
  `DELETE /api/v1/external-identities/{userId}` — a provider-side deletion signal is that call's to
  wire — which publishes
<!--#endif-->
  the `UserAccountDeleted` fact that every feature owning user data consumes — each applying the fate it
  declared for its own rows, re-checking through the account directory that the account is really
  gone — with a leased sweep behind the fact to catch a consumer that never ran. The template's
  features all delete their rows, and the same two faces are where a feature that prefers
  anonymising declares it. A feature added without a policy leaves its rows orphaned again, so
  `docs/ADDING-A-FEATURE.md` makes the declaration part of adding a feature that owns user data.
<!--#endif-->

<!--#if (localAuth)-->
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
<!--#endif-->
- **The background work needs an availability signal, and `/health/ready` is not it.** The jobs run
  in the API process, whose readiness probe answers for HTTP — the database checks and the shutdown
  state — so with every instance's jobs stopped, or switched off with `Jobs:Enabled=false`,
<!--#if (localAuth && reminders)-->
  no idempotency key is reclaimed, no expired refresh-token grant is deleted and no reminder is
  rung, and the probe stays green.
<!--#elseif (localAuth)-->
  no idempotency key is reclaimed and no expired refresh-token grant is deleted, and the probe stays
  green.
<!--#elseif (reminders)-->
  no idempotency key is reclaimed and no reminder is rung, and the probe stays green.
<!--#else
  no idempotency key is reclaimed, and the probe stays green.
#endif-->
  Each job emits a heartbeat, and it is what a deployment
  should alert on going flat, on the instance designated to run them:
  the `jobs.*.iterations` counters, counted once per pass and never
  gated on how much the pass found, so a healthy quiet system still produces samples. Alert on that
  instance's restart count alongside them.
<!--#if (reminders)-->
  Do **not** alert on the absence of `apptemplate.reminders.missed_cancellations`: that counter
  increments only when a cancellation was missed, so silence is its healthy state and it cannot tell
  a working loop from a dead one.
  Running the jobs on more than one instance is **safe** for the reminder pass.
<!--#endif-->
  Each purge is an idempotent delete over an already-covered range, so a second process wastes a
  connection and nothing more.
<!--#if (reminders)-->
  The reminder loop is where concurrency mattered: two replicas ticking
  in the same second read the same due reminders and both send the mail, and only then does one of
  them lose on `xmin` at its commit. No per-reminder claim prevents that — one held in memory is
  invisible to the other replica, and one written to the store before the send would only add a
  state that a crash strands — so the lease is the one exclusivity there is.
<!--#endif-->
  A pass that must not run twice takes `ILeaderLease`, whose adapter is a PostgreSQL session-level advisory lock
  on its own unpooled connection: exactly one host runs a pass, and losing that process closes the
  session and releases the lock rather than stranding a lease until a timer says otherwise. The
  guard is in the use case rather than in the `BackgroundService`, so it also covers any future
  caller
<!--#if (accounts)-->
  — `MaintenanceController` is the standing proof that one turns up.
<!--#else
  — and one does turn up.
#endif-->
  **It is not a fencing token.** Leadership can be lost mid-run without the work being told, so
  delivery stays at-least-once and anything else put under a lease has to survive a second host
  starting it. What the lease removes is the systematic duplication of every single pass.
  Still open, and the reason this entry stays here: nothing yet alerts when the loops stop.
<!--#if (localAuth)-->
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
- **Registration tells a caller whether an address is taken.** `POST /api/v1/auth/register`
  answers `409` `auth.register.unavailable` when the user name or the email is already in use; the
  authentication rate limit (10 a minute per client address by default) slows the reading and does
  not prevent it. Closing it means a silent registration that answers the same either way and mails
  the address's owner — a separate flow, which this template does not ship.
- **A rotation whose response is lost ends every session.** The rotation commits, the response never
  reaches the client, and the client presents the consumed token again: by the rules that is a
  replay, so every session of the account ends and the replay is recorded. It is the known cost of
  strict rotation; a grace window that accepted a just-consumed token would accept a stolen one too.
<!--#endif-->
- **Nothing proves the process actually drains on SIGTERM.** The readiness flip is tested, but
  Kestrel finishing in-flight requests and honouring `ShutdownTimeout` are asserted nowhere. Proving
  them requires running the binary and signalling it, which is a different kind of test than any here.
- **`If-Match` is optional by default.** Every read publishes a strong `ETag` and every write
  honours `If-Match`, refusing a stale or unrecognised version with `412`, but a request that sends
  no `If-Match` at all is still accepted unless `Concurrency:IfMatch` is set to `Required` — see
  `Concurrency:IfMatch`. Until you set it, a slow user's form submission can still overwrite a change made
  after it was rendered without anything detecting it.
- **Domain-event delivery is best-effort, with no outbox.** Consumers are isolated from one
  another: one throwing is logged with its event and consumer type, and the remaining consumers of
  that same event still run. Two losses are open — the throwing consumer's own
  side effect is never retried, and a process that dies between the commit and the dispatch
  loop loses every consumer for that save, because nothing durable recorded that the event was
  raised. Closing that needs an outbox, plus a dispatcher, a dead-letter path and idempotent
  consumers, and this template refuses to ship half of one: a dispatcher with no dead-letter queue
  and no alert on lag is worse than an acknowledged gap, because it looks solved.
<!--#if (reminders)-->
  The reminder feature shows the alternative, and it is the pattern to copy: the effect re-reads
  the state it depends on at the moment it acts, so a lost cancellation delays a reminder's
  retirement instead of firing it wrongly, and the reminder job counts every such divergence —
  a non-zero `apptemplate.reminders.missed_cancellations` is the number of events that went missing. A
  consumer whose effect *cannot* be re-derived, because nothing re-reads the state later — mail,
  money, a call to a third party — still needs an outbox before you rely on it.
<!--#else
  A consumer whose effect *cannot* be re-derived, because nothing re-reads the state later — mail,
  money, a call to a third party — still needs an outbox before you rely on it.
#endif-->
  No consumer is the only thing keeping a rule true: every effect re-derives its precondition when
  it runs, so a lost event leaves the system stale rather than wrong.
- **Idempotency keys expire only where the jobs run.** `Idempotency:Retention` stamps each row's
  `ExpiresAt`; it does not delete anything. The API's maintenance job calls the purge on its own
  schedule.
<!--#if (accounts)-->
  The same operation is exposed as
  `DELETE /api/v1/maintenance/idempotency-keys/expired` behind the `Administrator` policy for a
  deployment that runs the jobs on no instance. Deploy neither and a completed key stays replayable
  past its retention window while the table grows without bound.
<!--#else
  A deployment that runs the jobs on no instance has no purge at all, and a completed key stays
  replayable past its retention window while the table grows without bound.
#endif-->
<!--#if (localAuth)-->
- **The auth wire format has two owners.** `ConfigureJwtBearerOptions` in the Identity module builds
  its own `ProblemDetails` and owns the `auth.required` / `auth.forbidden` codes, so a change to how
  failures look must be made in two places.
- **Cancellation is not propagated through Identity.** `UserManager` and `SignInManager` accept no
  `CancellationToken`, so an abandoned request still runs its user-store I/O to completion.
- **The persisted security trail is not tamper-evident.** Sign-ins, lockouts, credential changes and
  session events are recorded in the `SecurityEvents` table, readable by the account's owner and an
  administrator and kept past account deletion for the retention window — but the table lives beside
  the business data, writable through the same connection, so it is a trail rather than an audit
  log: a report is exactly as trustworthy as the database user. Closing that properly requires an
  append-only store outside this application's own write path.
<!--#endif-->
