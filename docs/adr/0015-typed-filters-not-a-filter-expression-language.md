# 0015 — A typed filter surface, not a filter expression language

Status: Accepted

## Context

Collection endpoints such as `TodoListsController.GetAll` take `page` and `pageSize`
today. Filtering is being added alongside sorting and keyset pagination: a `search` term
matched case-insensitively against the list name via PostgreSQL `ILIKE`, with `%`, `_` and
`\` escaped before it reaches the query, and a `createdAfter`/`createdBefore` range. Each
is a named, typed query-string parameter bound to a primitive — a string or a
`DateTimeOffset` — and validated on its own.

The alternative on the table was a generic expression language: OData's `$filter`,
RSQL/FIQL, or a GraphQL-style `where` object that lets a caller assemble an arbitrary
boolean tree over arbitrary fields.

## Decision

**The query surface is a small, closed set of named, typed parameters. No parser accepts
a caller-composed predicate.** A feature that wants a new filter adds a parameter and a
`WHERE` clause for it; it does not extend a grammar that every other feature also gets.

## Consequences

- Every filter this API accepts can be read off the query record — there is no operator
  a client can invoke that is not visible in the type.
- The cost is real: adding a filter is a small code change per feature rather than a
  configuration change once, and a caller who wants `nameContains AND (createdAfter OR
  tagEquals)` cannot express it. Composed boolean logic across fields is out of scope by
  design, not by oversight.
- `search` is deliberately narrow — one column, one operator, metacharacters escaped
  before they reach `ILIKE` — precisely because it is the one free-text entry point.
  Widening it to more columns is a per-feature decision, not a configuration flag.

## Alternatives rejected

- **OData (`Microsoft.AspNetCore.OData`).** Gives a caller arbitrary boolean trees over
  whatever the provider exposes, so `contains` on an unindexed column is one query string
  away, and the field whitelist is enforced by the provider's configuration rather than
  provable by reading a record. It also brings its own metadata endpoint, its own query
  pipeline and its own error shapes, all of which would compete with the `Result`/
  ProblemDetails contract this template already standardises on ([0004](0004-result-as-the-failure-channel.md)).
- **A hand-written mini-language** (`name:contains:milk;createdAt:gt:2026-01-01`). Moves
  the same problem into a bespoke grammar this template would then have to parse, secure
  and document, for no more expressiveness than the typed parameters already give the
  features that need them.
- **A JSON predicate tree in the request body or query string.** The whitelist question
  gets worse, not better: proving that every tree a client can submit only ever reaches
  safe columns and safe operators is a claim about a parser, not about a record, and it
  has to be re-proven every time an operator is added.

## Revisit when

A feature genuinely needs caller-composed boolean logic across many fields. At that
point the honest answer is a dedicated search index built for that access pattern, not a
filter parser layered over the transactional store.
