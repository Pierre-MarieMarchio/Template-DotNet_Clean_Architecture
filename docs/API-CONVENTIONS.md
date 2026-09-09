# API conventions

What holds for every endpoint, whatever feature it belongs to. The endpoints themselves are in
[`API-REFERENCE.md`](API-REFERENCE.md); this page is what a client has to understand once and then
stops thinking about.

- [Versioning](#versioning)
- [Authorisation is default-deny](#authorisation-is-default-deny)
- [Errors are RFC 7807, and clients branch on `code`](#errors-are-rfc-7807-and-clients-branch-on-code)
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

`Program.cs` installs an authorization fallback policy requiring an authenticated
user. An endpoint is protected **unless it explicitly opts out** with
`[AllowAnonymous]`; ten of `AuthController`'s eighteen actions and the two health
endpoints do — and, in Development only, so do the two OpenAPI endpoints (see
[`GETTING-STARTED.md`](GETTING-STARTED.md#with-compose)).

One consequence to know about: because the fallback policy also applies when no
endpoint matched, an **unknown route returns 401 to an anonymous caller, not 404**.
That is not a bug, but it will surprise a client developer, so say so in your API
docs.

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

**Authentication and authorisation**

| `code` | Status |
|---|---|
| `auth.required` | 401 — no token, an expired one, or a route that matched nothing |
| `auth.forbidden` | 403 — authenticated, but the endpoint's policy is not satisfied, or the action refuses to target the caller's own account |
| `auth.login.invalidCredentials` | 401 — one answer for unknown address, wrong password, unconfirmed email and lockout alike |
| `auth.login.invalidTwoFactorChallenge` | 401 — the challenge token is unknown, spent or expired |
| `auth.refreshToken.invalid` | 401 — unknown, expired, revoked or replayed |
| `auth.externalSignIn.refused` | 401 — one answer for an unknown provider and a token that did not verify |
| `auth.register.unavailable` | 409 |
| `auth.confirmEmail.invalid` / `auth.resetPassword.invalid` / `auth.changeEmail.invalid` | 400 — the token is unknown, spent or expired |
| `auth.twoFactor.alreadyEnabled` | 409 |
| `auth.account.notFound` | 404 — an administration action naming an account that does not exist |
| `auth.account.cannotDeleteSelf` / `auth.lockout.cannotTargetSelf` / `auth.roles.cannotTargetSelf` / `auth.twoFactor.cannotTargetSelf` | 403 — an administrator may not aim these at their own account |
| `auth.account.deletionRejected` / `auth.lockout.rejected` / `auth.twoFactor.administrativeDisableRejected` | 409 — the store refused the change |

**Resources**

| `code` | Status |
|---|---|
| `todoList.notFound` / `todoItem.notFound` / `storedFile.notFound` / `reminder.notFound` | 404 — a resource owned by somebody else is also 404, so ids cannot be enumerated |
| `reminder.targetNotFound` | 404 — the item a reminder is about does not exist or is not the caller's |
| `storedFile.notAvailable` / `storedFile.quarantined` | 409 — the content has no verdict yet, or was examined and refused. The second never becomes available |
| `storedFile.depositMissing` | 409 — `confirm` found nothing deposited against the registration |
| `storedFile.quotaExceeded` | 409 — the caller's own pending, file-count or byte allowance |
| `domain.invariantViolated` | 409 via `DomainGuard`; 400 if a use case's own catch is missing and `DomainException` reaches `GlobalExceptionHandler` |

**The request itself**

| `code` | Status |
|---|---|
| `request.validationFailed` | 400 — a body or query value the API refused on the way in, with the offending fields in `errors`. Model binding and body validation share this code deliberately |
| `paging.invalid` / `sort.invalid` / `filter.invalid` / `cursor.invalid` | 400 — one bound of the collection contract, named so a client knows which rule it broke |
| `precondition.failed` | 412 — the `If-Match` a write named is stale, unrecognised, or `*` against a missing/foreign resource |
| `precondition.required` | 428 — only when `Concurrency:IfMatch` is `Required`; the write named no version at all |
| `precondition.malformed` | 400 — `If-Match` is present but is neither `*` nor a comma-separated list of quoted entity tags |
| `idempotency.keyInvalid` | 400 — the `Idempotency-Key` header is blank or over 128 characters |
| `idempotency.keyReused` | 409 — the same key with a different body |
| `idempotency.inProgress` | 409 — the first request under this key has not finished |
| `idempotency.notReplayable` | 409 — the stored response cannot be replayed |
| `rateLimit.exceeded` | 429, with a `Retry-After` header |
| `request.malformed` | 400 — a rejection from the framework or middleware that carries no more specific code |
| `request.failed` | The fallback, for a status none of the above names. Seeing it means a producer answered a status this table has no entry for |
| `request.methodNotAllowed` | 405 |
| `request.notAcceptable` | 406 |
| `request.tooLarge` | 413 — over `RequestLimits:MaxRequestBodyBytes`, refused before the body is buffered |
| `request.unsupportedMediaType` | 415 |
| `request.timeout` / `request.cancelled` | 408 / 499 |
| `route.notFound` | 404 — an authenticated caller on a route that matched nothing; an anonymous one gets `auth.required` instead |
| `server.unexpected` | 500 |

A 500 carries no exception text — only a sanitised message and a `traceId` that
correlates with the full stack trace in the logs.

Every error response is served as `application/problem+json`.

## Conditional requests — `ETag` and `If-Match`

Every read of a single list, item or file publishes that
resource's version as a strong, opaque `ETag`; every write of one — a reminder's
reschedule and cancel, a file's confirm and delete included — honours `If-Match`, so a
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
choice) on any of the four creates that carry `[Idempotent]`: `POST /api/v1/todo-lists`,
`POST /api/v1/todo-lists/{id}/items`, `POST /api/v1/todo-lists/{id}/items/{id}/reminders`
and `POST /api/v1/files`.

```bash
curl -X POST "$API/todo-lists" -H "Authorization: Bearer $TOKEN" \
     -H 'Idempotency-Key: 9f1c7e2a-0c1b-4f0a-9a3e-2b6d5c4a8e10' \
     -H 'Content-Type: application/json' -d '{"name":"Groceries"}'
```

| Situation | Answer |
|---|---|
| First use of the key | The request runs normally |
| Same key, same body, after it completed | The original status, body and `Location`, plus `Idempotency-Replayed: true` — the action does **not** run again |
| Same key, **different** body | `409` `idempotency.keyReused` |
| Same key while the first is still running | `409` `idempotency.inProgress` |
| Same key, but the first attempt **failed** | Runs normally — a failed attempt releases its claim, so a corrected retry is not blocked |
| No key at all | Runs normally; the header is available, not compulsory |

Keys are scoped **per user**, so two callers may use the same key string without colliding.
Only actions marked `[Idempotent]` participate — the auth endpoints deliberately do not,
because replaying a login would mean storing a bearer token in the database.

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

```bash
# newest first, then by name, second page of ten
GET /api/v1/todo-lists?sort=createdAt:desc,name:asc&page=2&pageSize=10

# name contains "grocer", case-insensitively, created this year
GET /api/v1/todo-lists?search=grocer&createdAfter=2026-01-01T00:00:00Z

# keyset paging: first page, then follow nextCursor
GET /api/v1/todo-lists?paging=cursor&pageSize=10&sort=name:asc
GET /api/v1/todo-lists?paging=cursor&pageSize=10&sort=name:asc&cursor=eyJmIjoibmFtZSI...
```

**Sortable fields are a whitelist, per feature.** `name`, `createdAt` and
`lastModifiedAt` — and nothing else. An unknown field, or one that exists on the row
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

**Search is bounded and happens in the database.** It is a case-insensitive contains
on the list name, via PostgreSQL `ILIKE` with `%`, `_` and `\` escaped, so a caller
sending `%` matches lists literally containing `%` rather than matching everything. It
is **not** accent-insensitive: that needs the `unaccent` extension and a functional
index, which is a deployment's decision, and doing it in memory instead would mean
reading every row to filter a page of twenty.

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
| `api/v1/auth/*` | 10 requests/minute per IP | 429 + `Retry-After: 60` + `code: rateLimit.exceeded` |
| Everything else | 300 requests/minute per IP | same |

Fixed windows, partitioned by `RemoteIpAddress`. **Behind a reverse proxy this needs the
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
healthchecks both target `/health`. What each probe should be wired to, and why readiness
fails the instant shutdown starts, is in
[`DEPLOYMENT.md`](DEPLOYMENT.md).

## Mail is written in the reader's language

Every mail this template sends — the three account mails and the reminder — ships one
template per language, and the **subject is that template's `<title>`**, so a subject and
a body can never end up in different languages. English and French ship.

| Where the language comes from | |
|---|---|
| `AppTemplate.Api` | The request's `Accept-Language`. The first well-formed tag wins; `q` values are not weighed, because a mail is written in one language. |
| A request that names none | `Localization:DefaultCulture`. |
| `AppTemplate.Worker` | `Localization:DefaultCulture`, always — a background pass has no request to read a preference from. |
| A language with no template | English, which every mail must ship. `fr-CA` reaches the `fr` template before falling back. |

```bash
curl -s -X POST http://localhost:8080/api/v1/auth/register \
  -H 'Content-Type: application/json' -H 'Accept-Language: fr' \
  -d '{"userName":"alice","email":"alice@example.com","password":"Passw0rd!x"}'
# the confirmation mail in mailpit now reads "Confirmez votre adresse e-mail"
```

**Adding a language is adding files.** Drop `<Mail>EmailTemplate.<tag>.html` beside the ones
in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/Auth/Templates/` and
`Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/Reminders/`, and that language
is available — there is no list to update, because a list could name a language no template
backs. `EmailTemplateCoverageTests` refuses a language added to one folder and not the other,
and refuses two languages of one mail sharing a subject.

**Two things to know before changing this.** The repository builds with
`InvariantGlobalization=true`, so there is no `CultureInfo` to carry a language in and
`AppTemplate.Application.Core.Common.Localization.CurrentLanguage` carries a BCP-47 tag instead.
And an `EmbeddedResource` named `*.fr.html` needs `WithCulture="false"` in the `.csproj`, or
MSBuild compiles it into a satellite assembly and every mail throws at the first send.
[`CONFIGURATION.md`](CONFIGURATION.md#localization) has the rest, including where a
stored per-account preference would plug in.

