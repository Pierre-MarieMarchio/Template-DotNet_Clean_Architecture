# 0010. An idempotency key is a lease per user

Status: Accepted

## Context

A client that sends a `POST` and loses the answer cannot tell whether the write happened. Retrying
blindly can create the resource twice. The `Idempotency-Key` header lets the client name the
attempt, so the server can answer a retry with the first response. The difficult parts are two
callers choosing the same key, a retry carrying a different body, and a server that dies between
claiming a key and finishing the work.

## Decision

- **Keyed by user and key.** A key is stored per (user, key), so two callers never collide. A
  request without an identified caller that sends a key is refused with
  `idempotency.callerNotIdentifiable`, rather than served without the protection it asked for.
- **A fingerprint tells a retry from a reuse.** The same key with a different request is
  `idempotency.keyReused`, a client error that never becomes reclaimable.
- **A claim is a lease.** The first request claims the key for a bounded time. A retry inside the
  lease is `idempotency.inProgress`; past it, one conditional `UPDATE` reclaims the row, so the
  database decides which of two retries wins. A completed response is kept for
  `Idempotency:Retention` and replayed as it was.
- **Claims commit on their own**, outside the request's unit of work, so they survive whether the
  business write commits or not. Once a response has started, its claim is kept.
- **Opt-in per action.** An action carries `[Idempotent]`, and only a `POST` may.
<!--#if (localAuth)-->
- **Never on the token endpoints.** Replaying a sign-in would keep an issued token pair at rest in
  a table built to be read back.
<!--#endif-->

## Consequences

- A client can retry any idempotent `POST` safely, and learns from the code whether it raced
  itself, reused a key or replayed a finished request.
- A response too large to keep answers a later retry with `idempotency.notReplayable` rather than a
  partial replay.
- `IdempotencyLeaseTests` proves the lease and the race against a real database;
  `IdempotentActionsAreAlwaysPostTests` holds the opt-in to `POST`.
- Expired keys are purged by a background job, in bounded batches.

[Idempotency-Key](../architecture/http-api.md#idempotency-key) is the client's contract.

## Revisit when

A client needs idempotency on a verb other than `POST`, or keys must be shared between callers, as
a service acting for many users would.
