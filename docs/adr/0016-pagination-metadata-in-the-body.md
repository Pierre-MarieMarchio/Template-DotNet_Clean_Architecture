# 0016 — Pagination metadata lives in the body, not in `Link` headers

Status: Accepted

## Context

`PagedResult<TItem>` (`Src/Application/AppTemplate.Application/Common/PagedResult.cs`)
already carries `Page`, `PageSize`, `TotalCount`, and derives `TotalPages` and
`HasNextPage` from them. Keyset pagination is being added alongside it, which means the
same envelope also carries an opaque cursor for the next page. RFC 8288 defines a second,
independent place to put exactly this kind of information: a `Link` response header with
`rel="next"`/`rel="prev"` values.

## Decision

**Page navigation is expressed once, in the `PagedResult<TItem>` envelope. No `Link`
header is emitted.**

## Consequences

- There is one statement of "is there a next page" and "what identifies it", not two that
  could disagree. A header built from stale state, or a body built from a different
  query than the header's URL encodes, is a class of bug this avoids by not having a
  second representation to keep in sync.
- The envelope is already the shape a client deserialises the response into, and it is
  where the cursor has to live anyway — a `Link` header could only ever repeat, in a
  different syntax, a fact the body already states.
- This is fair to what `Link` is good at, and gives it up deliberately: it is the actual
  standard, it lets a client that has never seen this API's parameter names still follow
  `rel="next"`, and it is what a generic HTTP client library knows how to consume without
  bespoke code. This API's clients are typed against the envelope already, so that
  affordance has no one to serve — and a hypermedia relation nobody follows is still
  surface that has to stay correct on every response.

## Alternatives rejected

- **`Link` header only, no cursor in the body.** Forces every client to parse a header
  to get information the body's shape already implies it should carry, for a client
  population that is typed against the body regardless.
- **Both.** Doubles the maintenance burden for a hypermedia affordance this API's actual
  clients do not use, and reintroduces the two-statements-of-one-fact problem the
  decision exists to avoid.

## Revisit when

A real client starts consuming pages without knowing this API's parameter vocabulary —
for example, a generic feed reader driven purely by relation types. That is the situation
`Link` exists for, and it is not the situation this API has today.
