# API conventions

What holds for every endpoint, whatever feature it belongs to. The endpoints themselves are in
[`API-REFERENCE.md`](API-REFERENCE.md); this page is what a client has to understand once and then
stops thinking about.

- [Versioning](#versioning)
- [Authorisation is default-deny](#authorisation-is-default-deny)
- [Errors are RFC 7807, and clients branch on `code`](#errors-are-rfc-7807-and-clients-branch-on-code)
- [Request bodies — JSON, and one multipart upload](#request-bodies--json-and-one-multipart-upload)
- [Conditional requests — `ETag` and `If-Match`](#conditional-requests--etag-and-if-match)
- [Retrying a `POST` safely — `Idempotency-Key`](#retrying-a-post-safely--idempotency-key)
- [Collection queries — sorting, filtering, paging](#collection-queries--sorting-filtering-paging)
- [Rate limits](#rate-limits)
- [Health](#health)
- [Mail is written in the reader's language](#mail-is-written-in-the-readers-language)

## Versioning

All routes are versioned. `api-supported-versions: 1.0` comes back on every response, and the
version segment substitutes into the route (`api/v{version:apiVersion}/…`). The path is the only
place the version is read from: the reader is `UrlSegmentApiVersionReader`, so an `?api-version=`
query string or an `x-ms-version` header is not a second way in — it is ignored, and the mandatory
path segment decides.

No response carries a `Deprecation` or `Sunset` header while one version ships, because they would
announce a schedule nobody has committed to. An architecture test holds that, and it is meant to be
deleted the day a second version exists.

## Authorisation is default-deny

<!--#if (accounts)-->
`Program.cs` installs an authorization fallback policy requiring an authenticated
user. An endpoint is protected **unless it explicitly opts out** with
`[AllowAnonymous]`; the two health endpoints do — and, in Development only, so do the
two OpenAPI endpoints (see [`GETTING-STARTED.md`](GETTING-STARTED.md#with-compose)).
<!--#if (localAuth)-->
Twelve of the twenty-eight authentication actions opt out as well, each one a caller
holding no token of ours has to reach.
<!--#endif-->

One consequence to know about: because the fallback policy also applies when no
endpoint matched, an **unknown route returns 401 to an anonymous caller, not 404**.
That is not a bug, but it will surprise a client developer, so say so in your API
docs.
<!--#else
`Program.cs` sets the fallback authorization policy to admit every caller: this API
authenticates nobody on purpose, and generating it without accounts made public access
a written choice rather than the side effect of a missing scheme. An endpoint marked
`[Authorize]` is refused at start-up rather than left failing closed at request time,
because no authentication scheme exists to challenge with, and an operation that needs
a caller still fails: `ICurrentUser` answers no one.
#endif-->

## Errors are RFC 7807, and clients branch on `code`

Every failure is a `ProblemDetails` body with a stable, dotted `code` extension
member. **Branch on `code`, never on the prose in `detail`** — the prose is not part
of the contract.

```json
{
  "type": "https://httpstatuses.io/404",
  "title": "Not found",
  "status": 404,
  "detail": "No to-do list with id '…' was found.",
  "code": "todoList.notFound"
}
```

<!--#if (accounts)-->
**Authentication and authorisation**

| `code` | Status |
|---|---|
| `auth.required` | 401 — no token, an expired one, a route that matched nothing, or a token whose account has since been deleted |
| `auth.forbidden` | 403 — authenticated, but the endpoint's policy is not satisfied |
<!--#if (localAuth)-->
| `auth.login.invalidCredentials` | 401 — one answer for unknown address, wrong password, unconfirmed email and lockout alike |
| `auth.login.invalidTwoFactorChallenge` | 401 — the challenge token is unknown, spent or expired |
| `auth.refreshToken.invalid` | 401 — unknown, expired, revoked or replayed |
| `auth.externalSignIn.refused` | 401 — one answer for an unknown provider and a token that did not verify |
| `auth.register.unavailable` | 409 |
| `auth.confirmEmail.invalid` / `auth.resetPassword.invalid` / `auth.changeEmail.invalid` | 400 — the token is unknown, spent or expired |
| `auth.twoFactor.alreadyEnabled` | 409 |
| `auth.account.notFound` | 404 — an administration action naming an account that does not exist |
| `auth.account.cannotDeleteSelf` / `auth.lockout.cannotTargetSelf` / `auth.roles.cannotTargetSelf` / `auth.twoFactor.cannotTargetSelf` | 403 — an administrator may not aim these at their own account |
| `auth.account.deletionRejected` / `auth.lockout.rejected` / `auth.twoFactor.administrativeDisableRejected` | 400 — the store refused the change |
| `auth.account.reAuthenticationRequired` | 403 — deleting the caller's own account, or registering a passkey on an account that offers no credential its holder could present, demands a proof; when the proof falls back to the access token itself, the token making the call is older than the five-minute window. Sign in again and retry |
| `auth.twoFactor.setupRejected` / `auth.twoFactor.enableRejected` / `auth.twoFactor.disableRejected` | 400 — the store refused the change, so the second factor is as it was |
| `auth.passkeySignIn.invalid` | 401 — one answer for a state string this API never issued, one expired or already used, a ceremony that did not verify, and an account no longer allowed to sign in |
| `auth.passkey.registrationInvalid` | 400 — the registration ceremony did not verify; attached to `credential` |
| `auth.passkey.registrationRejected` | 400 — the ceremony verified, but the store refused to hold the credential |
| `auth.passkey.notFound` | 404 — the passkey named is not one of the caller's, exactly as an unknown id or one already removed |
<!--#endif-->
<!--#if (externalAuth)
| `externalIdentity.mapping.notFound` | 404 — an administrative deletion naming an owner id no provider mapping carries |
#endif-->
<!--#endif-->

**Resources**

| `code` | Status |
|---|---|
<!--#if (todoLists)-->
| `todoList.notFound` / `todoItem.notFound` | 404 — a resource owned by somebody else is also 404, so ids cannot be enumerated |
<!--#endif-->
<!--#if (reminders)-->
| `reminder.notFound` | 404 — a resource owned by somebody else is also 404, so ids cannot be enumerated |
| `reminder.targetNotFound` | 404 — the item a reminder is about does not exist or is not the caller's |
<!--#endif-->
<!--#if (files)-->
| `storedFile.notFound` | 404 — a resource owned by somebody else is also 404, so ids cannot be enumerated. The one exception is the content of an available file whose category is `Authenticated` |
| `storedFile.notAvailable` / `storedFile.quarantined` | 409 — the content has no verdict yet (or, for an available file, the store no longer holds it), or was examined and refused. The second never becomes available |
| `storedFile.depositMissing` | 409 — `confirm` found nothing deposited against the registration |
| `storedFile.quotaExceeded` | 409 — the caller's own pending, file-count or byte allowance |
| `storedFile.quotaBusy` | 409 — in a `Strict` category, another registration of the caller in the same category held the quota lock for more than 10 seconds. Nothing was written; retry |
| `storedFile.directUploadUnavailable` | 409 — registration for a direct upload against a store that cannot sign URLs (the file system store), or in a category configured for uploads through the API only |
| `storedFile.apiUploadUnavailable` | 409 — an upload through the API (`POST /api/v1/files/uploads`) in a category configured for direct uploads only |
<!--#endif-->
<!--#if (notes)-->
| `note.notFound` | 404 — a resource owned by somebody else is also 404, so ids cannot be enumerated |
<!--#endif-->
| `domain.invariantViolated` | 409 via `DomainGuard`; 400 if a use case's own catch is missing and `DomainException` reaches `GlobalExceptionHandler` |

**The request itself**

| `code` | Status |
|---|---|
| `request.validationFailed` | 400 — a body or query value the API refused on the way in, with the offending fields in `errors`. Model binding and body validation share this code deliberately |
| `paging.invalid` / `sort.invalid` / `filter.invalid` / `cursor.invalid` | 400 — one bound of the collection contract, named so a client knows which rule it broke |
| `precondition.failed` | 412 — the `If-Match` a write named is stale, unrecognised, or `*` against a missing/foreign resource |
| `precondition.required` | 428 — only when `Concurrency:IfMatch` is `Required`; the write named no version at all |
| `precondition.malformed` | 400 — `If-Match` is present but is neither `*` nor a comma-separated list of quoted entity tags |
| `concurrency.conflict` | 409 — a lost update: the row changed between the read and the save, and the write was not conditioned on a version that would have caught it first. Reload and apply the change again |
| `idempotency.keyInvalid` | 400 — the `Idempotency-Key` header is blank or over 128 characters |
| `idempotency.callerNotIdentifiable` | 400 — an `Idempotency-Key` was sent on a request with no caller this server can identify. Keys are scoped per user, so there is nothing to scope it to |
| `idempotency.keyReused` | 409 — the same key with a different body |
| `idempotency.inProgress` | 409 — the first request under this key has not finished |
| `idempotency.notReplayable` | 409 — the stored response cannot be replayed |
| `rateLimit.exceeded` | 429, with a `Retry-After` header |
| `request.malformed` | 400 — a rejection from the framework or middleware that carries no more specific code |
| `request.failed` | The fallback, for a status none of the above names. Seeing it means a producer answered a status this table has no entry for |
| `request.methodNotAllowed` | 405 |
| `request.notAcceptable` | 406 |
| `request.tooLarge` | 413 — over `RequestLimits:MaxRequestBodyBytes`; refused before the body is read when it declares its length |
<!--#if (files)
| `request.tooLarge` | 413 — also over `Files:ApiUploadMaxBytes` plus 16 KiB on the upload endpoint; refused before the body is read when it declares its length |
#endif-->
| `request.unsupportedMediaType` | 415 |
| `request.timeout` / `request.cancelled` | 408 / 499 |
| `route.notFound` | 404 — an authenticated caller on a route that matched nothing; an anonymous one gets `auth.required` instead |
| `server.unexpected` | 500 |

A 500 carries no exception text — only a sanitised message and a `traceId` that
correlates with the full stack trace in the logs.

Every error response is served as `application/problem+json`.

## Request bodies — JSON, and one multipart upload

<!--#if (files)-->
Every request body is `application/json`, at most `RequestLimits:MaxRequestBodyBytes` (64 KiB by
default), with one exception: `POST /api/v1/files/uploads` takes `multipart/form-data` and carries
a file's bytes. Its limit is `Files:ApiUploadMaxBytes` (10 MiB by default, 30 MiB at most) plus
16 KiB of multipart framing, raised for that endpoint alone — on the declared length, on the
server's own limit and on the multipart body. Beyond it the answer is `413` `request.tooLarge`,
like any other oversized body. Any other content type on an endpoint is `415`
`request.unsupportedMediaType`.
<!--#else
Every request body is `application/json`, at most `RequestLimits:MaxRequestBodyBytes` (64 KiB by
default). Any other content type on an endpoint is `415` `request.unsupportedMediaType`.
#endif-->

**A feature that chooses `PATCH` writes a JSON Merge Patch** (RFC 7396), which is the recommended
default: a merge patch is applied onto the stored representation, and the merged result is what
the feature validates and writes — so an invariant is still checked against a whole resource and
never against a body whose meaning depends on which keys the client happened to send. The merge
goes through the resource's domain methods and invariants like any other write, never a blind
property copy; `null` removes a member, and a member the patch omits is unchanged. Another patch
format has to state its own absent-and-unchanged rule before it can be validated, which is why
none is recommended.

## Conditional requests — `ETag` and `If-Match`

**This is a property of versioned resources, not of every resource.** A resource is versioned
when its storage keeps a concurrency token, and a feature opts in by keeping one; each feature's
section of [`API-REFERENCE.md`](API-REFERENCE.md) says whether it does. A resource that keeps
none publishes no `ETag`, honours no `If-Match`, and its writes are
last-write-wins — a feature added without a version column must not be documented as protected, so
nothing here claims it is. A collection is never versioned: a page of summaries carries no `ETag`,
and a create has no `If-Match`, because there is no resource yet to name.

Every read of a single versioned resource publishes that
resource's version as a strong, opaque `ETag`; every write of one honours
`If-Match`, so a
stale edit, decided against a version somebody else has since changed, is refused with
`412` instead of silently overwriting it. `If-Match: *` asserts that the resource
exists, so a missing or someone-else's resource also answers `412`, not `404`. Sending
no `If-Match` at all is accepted unless `Concurrency:IfMatch` is set to `Required`, in
which case it is refused with `428` — see
[`CONFIGURATION.md`](CONFIGURATION.md#concurrency). [`AppTemplate.Api.http`](../AppTemplate.Api.http) walks
through the whole round trip.

## Retrying a `POST` safely — `Idempotency-Key`

A client that retries a create through a flaky network must not create twice. Send an
`Idempotency-Key` header (any opaque string up to 128 characters — a UUID is the obvious
choice) on any create that carries `[Idempotent]`:
<!--#if (todoLists)-->
- `POST /api/v1/todo-lists`
- `POST /api/v1/todo-lists/{id}/items`
<!--#endif-->
<!--#if (reminders)-->
- `POST /api/v1/todo-lists/{id}/items/{id}/reminders`
<!--#endif-->
<!--#if (files)-->
- `POST /api/v1/files`
<!--#endif-->
<!--#if (notes)-->
- `POST /api/v1/notes`
<!--#endif-->

<!--#if (todoLists)-->
```bash
curl -X POST "$API/todo-lists" -H "Authorization: Bearer $TOKEN" \
     -H 'Idempotency-Key: 9f1c7e2a-0c1b-4f0a-9a3e-2b6d5c4a8e10' \
     -H 'Content-Type: application/json' -d '{"name":"Groceries"}'
```
<!--#endif-->

| Situation | Answer |
|---|---|
| First use of the key | The request runs normally |
| Same key, same body, after it completed | The original status, body (none for a `204`) and `Location`, plus `Idempotency-Replayed: true` — the action does **not** run again |
| Same key, **different** body | `409` `idempotency.keyReused` |
| Same key while the first is still running | `409` `idempotency.inProgress` |
| Same key, but the first attempt **failed** | Runs normally — a failed attempt releases its claim, so a corrected retry is not blocked |
| No key at all | Runs normally; the header is available, not compulsory |

Keys are scoped **per user**, so two callers may use the same key string without colliding.
Only actions marked `[Idempotent]` participate.
<!--#if (localAuth)-->
The auth endpoints deliberately do not, because replaying a login would mean storing a bearer token
in the database.
<!--#endif-->
<!--#if (files)-->
Neither does `POST /api/v1/files/uploads`, whose body is the file: a retried upload creates a
second file.
<!--#endif-->

A completed key stays replayable for `Idempotency:Retention`, but **nothing prunes the
table for you**: schedule `DELETE /api/v1/maintenance/idempotency-keys/expired`. That
window, the claim lease behind `idempotency.inProgress` and the rest of the section are in
[`CONFIGURATION.md`](CONFIGURATION.md#idempotency).

## Collection queries — sorting, filtering, paging

The collection endpoint takes the same contract every collection endpoint in this
template should take. It is deliberately a **closed** contract: a caller may only ask
for what a feature has declared, and anything else is a `400` with a stable `code`
rather than a clamp, a guess or a 500.

| Parameter | Type | Default | Bound |
|---|---|---|---|
| `sort` | `field[:asc\|:desc]`, comma-separated | `createdAt:desc` | ≤ 3 terms, each a whitelisted field, no field twice |
| `search` | text, matched against the list **name** | none | ≤ 100 characters |
| `createdAfter` / `createdBefore` | ISO 8601 instant | none | `createdAfter` must not be later than `createdBefore` |
| `paging` | `offset` or `cursor` | `offset` | — |
| `page` | integer, 1-based | `1` | ≥ 1, offset mode only |
| `pageSize` | integer | `20` | 1…100 |
| `cursor` | opaque token from `nextCursor` | none | cursor mode only, ≤ 512 characters |

<!--#if (todoLists)-->
The examples below are the to-do lists'.
<!--#endif-->
<!--#if (notes && !todoLists)-->
`Notes` answers this contract with `title` and `createdAt` as its sortable fields and no filter.
<!--#endif-->

<!--#if (todoLists)-->
```bash
# newest first, then by name, second page of ten
GET /api/v1/todo-lists?sort=createdAt:desc,name:asc&page=2&pageSize=10

# name contains "grocer", case-insensitively, created this year
GET /api/v1/todo-lists?search=grocer&createdAfter=2026-01-01T00:00:00Z

# keyset paging: first page, then follow nextCursor
GET /api/v1/todo-lists?paging=cursor&pageSize=10&sort=name:asc
GET /api/v1/todo-lists?paging=cursor&pageSize=10&sort=name:asc&cursor=eyJmIjoibmFtZSI...
```
<!--#endif-->

**Sortable fields are a whitelist, per feature.**
<!--#if (todoLists)-->
The to-do lists ship `name`, `createdAt` and `lastModifiedAt` — and nothing else.
<!--#endif-->
An unknown field, or one that exists on the row
but is not on the list, is `400` `sort.invalid` with the legal names in the message. No
caller string ever reaches a LINQ expression: the field name is canonicalised against
the whitelist in the application layer, and the persistence layer turns it into a
column with an exhaustive `switch` whose `default` arm throws. A field is on the
whitelist only if it is cheap to order by, which is why the list is short and why each
entry has a composite index behind it (`(OwnerId, <field>, Id)`).

**Every order ends in a unique tiebreaker.** `Id` is appended to every `ORDER BY`,
always. Without it two rows with equal sort keys can swap places between two page
reads, so one row is served twice and another never — which is a silent wrong answer,
not a slow one.

**Search is bounded and happens in the database.**
<!--#if (todoLists)-->
For the to-do lists it is a case-insensitive contains
on the list name, via PostgreSQL `ILIKE` with `%`, `_` and `\` escaped, so a caller
sending `%` matches lists literally containing `%` rather than matching everything. It
is **not** accent-insensitive: that needs the `unaccent` extension and a functional
index, which is a deployment's decision, and doing it in memory instead would mean
reading every row to filter a page of twenty.
<!--#endif-->

**Two paging modes, and the difference matters.**

- **Offset** (`page`/`pageSize`) answers `totalCount`, `totalPages` and `hasNextPage`,
  which is what a page-number UI needs. It is unstable under concurrent writes — a row
  inserted before your position shifts everything down, so page 2 can repeat a row from
  page 1 — and it gets slower the deeper you go, because the database still walks the
  rows it skips.
- **Cursor** (`paging=cursor`, then follow `nextCursor`) resumes from the last row it
  served, so an insert elsewhere cannot shift your position, and page 500 costs what
  page 1 costs. It answers **no `totalCount`**: counting the whole match set is a second
  scan of it, which is the cost keyset paging exists to avoid. It allows **one** sort
  term (plus the tiebreaker), and only over a field marked keyset-capable —
  `lastModifiedAt` is not, because it is nullable and a comparison against `NULL` would
  skip the row the cursor was minted from instead of resuming at it.

The cursor is opaque but **not signed**. It does not need to be: it carries only values
from a row the caller was already served, and the read query filters by owner regardless
of what the cursor claims. A tampered cursor is a `400` `cursor.invalid`, never a 500
and never another user's rows.

**Pagination metadata lives in the body**, in the `PagedResult` envelope — there are no
RFC 8288 `Link` headers, so there is exactly one statement of where the next page is.
One statement of "is there a next page", in the body every client already parses.

Every bound above is a `400` carrying its own code — `paging.invalid`, `sort.invalid`,
`filter.invalid`, `cursor.invalid` — so a client can tell which rule it broke. A value
of the wrong *type* (`page=abc`) never reaches the application layer: model binding
refuses it with `request.validationFailed` and names the field in `errors`. That is the
same code a failed body validation carries, deliberately — one vocabulary for "this
request was rejected on its way in", and a specific code for each rule that has a
different remedy.

To give a new feature this contract, it declares an `ICollectionPolicy` — see
[`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md). Why the filter surface is typed rather than an expression
language is that a grammar the client composes is a query planner you then own.

## Rate limits

| Scope | Limit | On rejection |
|---|---|---|
<!--#if (localAuth)-->
| `api/v1/auth/*` | 10 requests/minute per IP (default) | 429 + `Retry-After: 60` + `code: rateLimit.exceeded` |
<!--#endif-->
| Everything else | 300 requests/minute per IP (default) | same |

Fixed windows, partitioned by `RemoteIpAddress`. The numbers are the defaults of the `RateLimiting`
section. **Behind a reverse proxy this needs the
`ReverseProxy` section turned on**, otherwise every request appears to come from the proxy
and the whole world shares one partition. It is off by default because the trust list
depends on your topology — and the validator refuses to start with `Enabled: true` and both
lists empty, which would accept a forged `X-Forwarded-For` from anybody. The counters are
also in-process, so the limit a caller meets is the number above times your replica count.
Both points are argued in full in
[`CONFIGURATION.md`](CONFIGURATION.md#reverseproxy).

## Health

| Route | Checks | Anonymous |
|---|---|---|
| `/health` | nothing — answers "is the process up" | yes |
| `/health/ready` | the database and the shutdown state, both tagged `ready` | yes |

Liveness deliberately touches no dependency, so an orchestrator does not restart a
healthy API because the database was briefly unreachable. The Compose and Dockerfile
healthchecks both target `/health`. Readiness fails when the database is unavailable or shutdown
starts; a deployment that routes traffic through a readiness probe should use `/health/ready`.

## Mail is written in the reader's language

Every mail this project sends ships one template per language, and the **subject is that
template's `<title>`**, so a subject and a body can never end up in different languages. English
and French ship.

| Where the language comes from | |
|---|---|
| `AppTemplate.Api` | The request's `Accept-Language`. The well-formed tag with the highest `q` weight wins (a missing `q` is 1, `q=0` excludes a tag, ties keep header order), as RFC 9110 negotiation says; a mail is written in one language, so only that tag is used. |
| A request that names none | `Localization:DefaultCulture`. |
| A background job | `Localization:DefaultCulture`, always — a background pass has no request to read a preference from. |
| A language with no template | English, which every mail must ship. `fr-CA` reaches the `fr` template before falling back. |

<!--#if (localAuth)-->
```bash
curl -s -X POST http://localhost:8080/api/v1/auth/register \
  -H 'Content-Type: application/json' -H 'Accept-Language: fr' \
  -d '{"userName":"alice","email":"alice@example.com","password":"Passw0rd!x"}'
# the confirmation mail in mailpit now reads "Confirmez votre adresse e-mail"
```
<!--#endif-->

<!--#if (reminders && localAuth)-->
**Adding a language is adding files.** Drop `<Mail>EmailTemplate.<tag>.html` beside the ones
in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/Accounts/Resources/Templates/` and
`Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/Reminders/`, and that language
is available — there is no list to update, because a list could name a language no template
backs. `EmailTemplateCoverageTests` refuses a language added to one folder and not the other,
and refuses two languages of one mail sharing a subject.
<!--#elseif (localAuth)
**Adding a language is adding files.** Drop `<Mail>EmailTemplate.<tag>.html` beside the ones
in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/Accounts/Resources/Templates/`, and that language
is available — there is no list to update, because a list could name a language no template
backs. `EmailTemplateCoverageTests` refuses a language added to one mail folder and not to every
other, and refuses two languages of one mail sharing a subject.
#elseif (reminders)
**Adding a language is adding files.** Drop `<Mail>EmailTemplate.<tag>.html` beside the ones
in `Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/Reminders/`, and that language is
available — there is no list to update, because a list could name a language no template backs.
The identity provider writes its own mail, so the reminder is the only one this project renders.
#else
**Adding a language is adding files**, once this project sends a mail: a module that renders one
embeds `<Mail>EmailTemplate.<tag>.html` per language, and that language is available. Generated
without the supplied accounts and without `Reminders`, this project ships no mail template yet.
#endif-->

**Two things to know before changing this.** The repository builds with
`InvariantGlobalization=true`, so there is no `CultureInfo` to carry a language in and
`AppTemplate.Application.Core.Common.Localization.CurrentLanguage` carries a BCP-47 tag instead.
And an `EmbeddedResource` named `*.fr.html` needs `WithCulture="false"` in the `.csproj`, or
MSBuild compiles it into a satellite assembly and every mail throws at the first send.
[`CONFIGURATION.md`](CONFIGURATION.md#localization) has the rest, including where a
stored per-account preference would plug in.
