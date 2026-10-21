# 0008. Optimistic concurrency, end to end

Status: Accepted

## Context

Two callers that read the same resource and then write it will overwrite each other unless
something notices. Locking a row between a read and a write is impossible over HTTP; a version
column maintained by the application is one more thing every write must remember. A version that
reaches the client lets the client say which state it decided against.

## Decision

- **The version is PostgreSQL's `xmin`.** The process never writes it. It is read by the same query
  that reads the value, so the version always describes the body it travels with.
- **It travels as `ETag`.** A response carrying a versioned resource returns its version; the
  resource's representation carries no version field.
- **`If-Match` is checked when sent.** The write compares it with the state it has just loaded and
  answers `412` when they differ. `Concurrency:IfMatch` is `Optional` by default;
  `Required` makes a write without it a `428`.
- **The database has the last word.** The version goes into the `WHERE` of the `UPDATE` or
  `DELETE`. A row that moved meanwhile is a `ConcurrencyConflictException`, answered `409`, and
  never retried: a retry would re-apply a decision made against a state that no longer exists.
- **A feature chooses.** The generator's `--concurrency etag` gives a feature this whole chain, and
  `--model aggregate` requires it; `--concurrency none` leaves a simple feature's writes last-wins.

## Consequences

- Lost updates are refused for every versioned feature, whether or not the client sends `If-Match`.
- A client that wants to be told before it overwrites sends `If-Match`, and handles `412` and `409`
  by reading again.
- `EntityTagMappingTests` and `IfMatchPreconditionTests` hold the header contract.
<!--#if (todoLists)-->
  `OptimisticConcurrencyTests` proves the conflict against a real database.
<!--#endif-->
<!--#if (notes)-->
  `NoteConcurrencyTests` proves it for the simple shape.
<!--#endif-->

[ETag and If-Match](../architecture/http-api.md#etag-and-if-match) and
[Concurrency](../architecture/persistence-model.md#concurrency) describe the mechanics.

## Revisit when

A client needs changes merged rather than refused, such as several people editing one document at
once.
