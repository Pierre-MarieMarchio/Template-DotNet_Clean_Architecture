---
type: decision
title: 0010. An idempotency key is a lease per user
description: Why an idempotency key is stored per user and claimed as a lease, so a retried create is answered once and a reused key is refused.
status: Accepted
---

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
- **Every success completes its key.** A `2xx` is kept whatever the action returned; only a failure
  status or an exception releases the claim.
- **Opt-in per action.** An action carries `[Idempotent]`, and only a `POST` may.
<!--#if (localAuth)-->
- **Never on the token endpoints.** Replaying a sign-in would keep an issued token pair at rest in
  a table built to be read back.
<!--#endif-->

## Consequences

- A client can retry any idempotent `POST` safely, and learns from the code whether it raced
  itself, reused a key or replayed a finished request.
- A success whose body is not kept, too large or not returned as an object (a file, a text, an empty
  `200`), answers a later retry with `idempotency.notReplayable` rather than a partial replay or a
  second run.
- At most once does not hold between the business commit and the stored response. The action's use
  case commits through the request's unit of work, and `IdempotencyStore.CompleteAsync` then keeps
  the response through a context of its own, a second commit. A process that stops between the two
  leaves the key claimed: a retry answers `idempotency.inProgress` until `Idempotency:ClaimLease`
  (15 minutes by default) has passed, then reclaims the key and runs the action again, so the write
  can happen twice.
- An exception thrown after the business commit and before the response starts, by a result that
  fails to serialize for one, releases the claim like any other failure: the filter cannot tell it
  from one thrown before the commit, and a retry runs the action again.
- `IdempotencyLeaseTests` proves the lease and the race against a real database;
  `IdempotentActionTests` holds the opt-in to `POST`; `IdempotencyFilterTests` holds that every
  success completes its key.
- Expired keys are purged by a background job, in bounded batches.

[Idempotency-Key](../architecture/http-api.md#idempotency-key) is the client's contract.

## Revisit when

A client needs idempotency on a verb other than `POST`, or keys must be shared between callers, as
a service acting for many users would.
