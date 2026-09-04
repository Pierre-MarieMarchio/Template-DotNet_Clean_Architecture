# 0023 — No security stamp cache

Status: Accepted

## Context

`ConfigureJwtBearerOptions.OnTokenValidated`
(`Src/Infrastructure/AppTemplate.Infrastructure.Identity/Bearer/ConfigureJwtBearerOptions.cs`)
calls `ValidateSecurityStampAsync` on every request carrying a bearer token. That check is what
makes a password change or a forced sign-out take effect before the access token expires: without
it, a token stays usable for its full lifetime no matter what happened to the account.

The cookie half of ASP.NET Identity does not do this. `SecurityStampValidator` re-reads the stamp
on a `ValidationInterval`, thirty minutes by default, precisely because the read was judged too
expensive to do per request. Copying that design here — a short TTL, invalidated at the points
where the stamp rotates — was the obvious next step, and it is the one being refused.

Two measurements moved the decision:

- The check is a primary-key lookup on the `AppDbContext` **scoped to the request**, not a search.
  The entity it loads lands in that request's change tracker, so a later read of the same user —
  `GET /auth/me` is the whole of one — is served from memory. The marginal cost is a pooled
  connection acquisition on endpoints that would otherwise touch no database at all, which is a
  connection-pool argument, not a CPU one.
- "Invalidated at the points of rotation" does not survive contact with more than one instance.
  A rotation handled by instance A evicts A's entry and nothing else. A client does not choose its
  instance, so the guarantee a caller can actually rely on is still "in at most TTL" — the
  invalidation adds no observable promise.

## Decision

**The security stamp is read from the database on every authenticated request. There is no cache,
and no `ValidationInterval`.**

## Consequences

- Revocation is immediate and uniform. A password change, a reset, or a forced sign-out fails
  every access token in circulation at the next request, on every instance, with no window to
  reason about. `Tests/Integration/AppTemplate.Api.IntegrationTests/Security/SecurityStampRotationTests.cs`
  holds that property.
- Every authenticated request takes a connection from a pool bounded by `Database:MaxPoolSize`.
  This is the cost being paid, and it is the number to watch: a saturated pool shows up as uniform
  latency across every endpoint, indistinguishable from a slow database.
- Caching per point of rotation would behave differently in development and in production. With
  one process, eviction makes revocation look instant; with N, it is only ever "within TTL". A
  feature that is exercised in the configuration where it does not matter, and unexercised in the
  one where it does, is the same trap as a serialisation test that names its own static type.
- A sliding expiration would be worse than a wrong TTL. It only elapses during **inactivity**, so
  an attacker holding a stolen token refreshes the entry with every request and the entry never
  expires while the attack lasts — revocation would take effect once the attacker stopped.

## When to revisit

When connection-pool saturation is **measured** — not assumed — as the binding constraint. The
answer then is an absolute TTL of a few tens of seconds, no invalidation at the rotation points,
and an `IMemoryCache` with a `SizeLimit` so that the number of active users cannot drive process
memory. State the guarantee as "a rotation takes effect in at most the TTL, everywhere", which is
uniform and can be tested by moving a clock, and write the test that proves a token still lives
just under the TTL — otherwise the test cannot fail.
