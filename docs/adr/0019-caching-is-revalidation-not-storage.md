# 0019 — Caching is revalidation, not storage

Status: Accepted

## Context

Every read of a `TodoList` or `TodoItem` already publishes a strong `ETag` built from the
aggregate's version (`TodoListsController.Validated`,
`Src/Presentation/AppTemplate.Api/Features/TodoLists/Controllers/TodoListsController.cs`),
and a request carrying a matching `If-None-Match` gets `304` with no body. That mechanism
was published with no `Cache-Control` header at all, which left an unstated question: may
anything actually store the response the `ETag` was attached to?

Reads are gaining an explicit `Cache-Control: private, no-cache` alongside the sorting and
filtering work. `no-cache` does not mean "do not cache" despite the name — it means "cache
if you like, but revalidate with the origin before reusing it," which is precisely what an
`If-None-Match` round trip does.

## Decision

**Reads send `Cache-Control: private, no-cache`.** `private` confines storage to the
end client, never a shared cache. `no-cache` permits that client to store the response but
requires it to revalidate before reuse — which is what makes the `ETag` this API already
publishes worth having, rather than a header nothing consults.

## Consequences

- A client that stores a response and later needs it can send `If-None-Match` and get
  `304`, saving the body transfer while still getting a correctness check on every use.
- `no-store` was considered and would have been wrong here: it forbids the very storage
  that makes revalidation possible, so pairing it with an `ETag` would ship a validator
  with nothing to validate.
- Sending nothing, the previous state, was also wrong: an unmarked authenticated response
  can be stored by a shared cache on a heuristic basis (RFC 9111 permits heuristic
  freshness absent explicit directives), which is exactly the one-user's-rows-served-to-
  another failure mode `private` exists to rule out.
- This is a header on the response, not a guarantee enforced against every cache in
  existence — a cache that ignores `private` was already a cache this API could not trust,
  with or without this header.

## Alternatives rejected

- **ASP.NET Core output caching (`AddOutputCache`/`UseOutputCache`).** Every response on
  this surface is scoped to the caller's own rows, so a shared output cache either serves
  one user's data to another or has to be keyed per user — at which point it is a
  per-user memory cache, and the hit rate such narrow keying produces does not justify the
  invalidation problem it creates the moment any write happens.
- **Response caching middleware.** It already respects `private` — which means it treats
  every response here as not cacheable, making it a no-op layer that adds configuration
  with nothing behind it.
- **A CDN in front of the API.** Solves nothing for per-user authenticated responses and
  reintroduces the shared-cache problem output caching has, one hop further from the
  origin and harder to purge correctly.

## Revisit when

An endpoint appears whose response is identical for every caller regardless of identity —
a catalogue or reference-data endpoint, say. That response can carry `public` and a real
`max-age`, and is the first candidate for output caching or a CDN; nothing on the
`TodoLists` surface is that today.
