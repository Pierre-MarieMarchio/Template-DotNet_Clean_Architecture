# 0008 — Default-deny authorisation

Status: Accepted

## Context

Authorisation was per-action: every controller method was individually responsible for
remembering `[Authorize]`. **Fifteen endpoints across the to-do list controllers were
reachable anonymously** because each of them forgot — including the ones that created,
renamed and deleted data.

This is the predictable outcome of an opt-in security control. Nothing about forgetting an
attribute fails a build, fails a test, or looks wrong in review; the endpoint simply works,
for everyone.

The read side made it worse: `GetAll` returned every user's rows regardless of caller, so
an anonymous request to a list endpoint returned the whole table.

## Decision

`Program.cs` installs an **authorization fallback policy requiring an authenticated
user**:

```csharp
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
```

An endpoint with no authorisation metadata is therefore denied. Being reachable
anonymously requires an explicit `[AllowAnonymous]`, and only three places have one:
`AuthController` (at class level), `/health` and `/health/ready`.

Ownership is enforced separately and structurally: both `ITodoListQueries` methods take the
owner's id as a parameter, so "only the caller's own rows" is part of the port's signature
rather than something an implementation might omit.

## Consequences

- **Forgetting is now safe.** A new controller added without thinking about authorisation
  is protected. The failure mode inverted from "silently public" to "returns 401 until you
  think about it", which is the only acceptable direction for a security default.
- The opt-out is visible. `[AllowAnonymous]` in a diff is a reviewable event; a missing
  `[Authorize]` is not.
- **An unknown route returns 401 to an anonymous caller, not 404.** The fallback policy
  also applies when routing matched no endpoint. Verified by request. This is a real
  behavioural change and will surprise a client developer, so document it in your API
  reference. It has a small upside — an unauthenticated caller cannot probe which routes
  exist — and one real cost: a typo in a URL reads as an auth problem.
- **Anything mapped outside a controller must opt out explicitly, and it is easy to
  forget.** `/health` and `/health/ready` do. `MapOpenApi()` and `MapScalarApiReference()`
  currently do **not**, so `/openapi/v1.json` and `/scalar/v1` answer 401 in Development.
  Verified by request. That is a live defect, and it is also the clearest illustration of
  the trade-off this decision makes: the cost of default-deny is paid by non-endpoint
  infrastructure, which is exactly where it is cheap to notice and fix.
- Authentication and authorisation must both be in the pipeline in the right order
  (`UseAuthentication` before `UseAuthorization`), or every request fails. That is now a
  smoke test rather than a subtle regression.

## Alternatives rejected

- **`[Authorize]` on every action** (what was there). Depends on fifteen separate acts of
  remembering, and it failed fifteen times out of fifteen.
- **`[Authorize]` at controller level.** Better, and still opt-in: a new controller
  without the attribute is public. Same failure mode, lower frequency.
- **A global `AuthorizeFilter` added to MVC options.** Roughly equivalent for controllers,
  but it does not cover endpoints mapped outside MVC — so health checks, OpenAPI and any
  future minimal API would be silently public. The fallback policy covers everything
  routing produces, which is the property worth having.
- **`RequireAuthorization()` on each `Map*` call.** Explicit and correct where applied,
  and it is opt-in again, one call site at a time.
- **A middleware that rejects unauthenticated requests before routing.** Blunt: it cannot
  distinguish the health endpoint from a data endpoint without a hard-coded path list,
  which then drifts from the routes.

## Revisit when

Never, for the default itself. What *should* be revisited is the opt-out list: it must stay
short, and every addition to it deserves the same scrutiny as a new public endpoint —
because that is what it is. Auditing `[AllowAnonymous]` and `.AllowAnonymous()` occurrences
is a good architecture test.
