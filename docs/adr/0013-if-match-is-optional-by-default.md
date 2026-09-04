# 0013 — `If-Match` is required by configuration, not by default

Status: Accepted

## Context

The `TodoList` aggregate already had `xmin`-based optimistic concurrency (see
`SECURITY.md`), but that guard only closes the window between a use case's own read and
its own commit. It cannot close the much longer window between a user opening an edit
form and submitting it, because nothing in that later request says which version the
change was decided against — the use case's read happens after the user already decided
what to write, so by the time it runs, the version it compares against is *always*
current.

Every read of a `TodoList` or `TodoItem` now publishes the aggregate's version as a
strong, opaque `ETag`, and every write honours a caller-supplied `If-Match`: a request
naming a stale version, a version this API never issued, or `If-Match: *` against a
missing or someone else's resource is refused with `412 Precondition Failed`. That much
is unconditional — it is pure upside, since a client that never sends `If-Match` is
simply not compared against anything and behaves exactly as before.

The open question is what happens when a client sends **no** `If-Match` at all. Two
answers are defensible:

- Accept the write anyway (the header is a capability the client may or may not use).
- Refuse it with `428 Precondition Required` (the header is a requirement every client
  must satisfy).

Refusing unconditionally-by-default would make this template correct out of the box for
new deployments, but it would also make it a breaking change for **every already-running
client of every existing deployment that adopts this template version**, because none of
them sends `If-Match` today. There is no way to distinguish "an old client that cannot be
updated yet" from "a client that was never taught to read a version" from inside the
request.

## Decision

**`Concurrency:IfMatch` defaults to `Optional`.** An unconditional write is accepted, and
the aggregate's own `xmin` check remains the only guard against a lost update. Setting it
to `Required` makes every mutating endpoint refuse a request with no `If-Match` with
`428 Precondition Required`, naming the header in the problem detail so a client that
never sends one has something to act on.

```json
{
  "Concurrency": { "IfMatch": "Required" }
}
```

Reads are never gated by this setting — `AReadWithoutAnIfMatch_IsStillServed` pins that a
client must always be able to obtain a validator, which is the only way it can ever
satisfy `Required`.

The setting is bound once at composition time (`ConcurrencyOptionsValidator`, validated
on start), not read per request, and not derived from anything about the caller. A
deployment picks one answer for its whole client population.

## Consequences

- **A fresh deployment stays exactly as exposed to lost updates as it was before this
  feature**, until whoever runs it makes a deliberate choice to turn `Required` on. That
  is the point: adding `If-Match` support must not silently start rejecting every
  deployed client that predates it.
- **The lost-update guarantee this feature exists to provide is opt-in.** A deployment
  that leaves the default in place has not closed the read-then-write race; it has only
  made closing it *possible* for clients that choose to send `If-Match`. This is recorded
  in `SECURITY.md` rather than left to be discovered.
- Turning `Required` on is a coordinated rollout, not a config flip taken lightly: every
  client the deployment serves has to read before it writes and echo the `ETag` back, or
  it starts seeing `428` on every mutation.
- Two integration test hosts are needed to cover this (`IfMatchRequiredTests` builds a
  second `WebApplicationFactory` with the setting overridden), because the setting is
  read at composition time and the shared fixture's host has to stay at the shipped
  default for every other test.

## Alternatives rejected

- **`Required` by default.** Correct for a template starting from nothing, wrong for a
  template whose whole purpose is to be dropped into an existing service with existing
  clients. The rejected cost was not hypothetical: it is exactly the population of
  clients that cannot be enumerated from inside this decision.
- **Infer the requirement from whether a client ever sends `If-Match`.** Would need
  per-client state this API has no reason to keep, and a client that sometimes forgets
  would sometimes be protected and sometimes not — a guarantee that only sometimes holds
  is not one.
- **Gate `Required` per route or per aggregate instead of globally.** `TodoList` is the
  only versioned aggregate this template ships, so there is nothing yet to differentiate;
  a per-route switch would be speculative surface with one call site exercising it.

## Revisit when

A second versioned aggregate ships with different client-migration timing than
`TodoList` — at that point a single global switch stops describing the deployment
accurately, and the setting needs to move from `Concurrency:IfMatch` to something keyed
per aggregate.
