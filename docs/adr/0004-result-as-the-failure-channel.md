# 0004 — `Result` as the failure channel for expected outcomes

Status: Accepted

## Context

The previous code signalled every failure with an exception. "No list with that id" threw
`InvalidOperationException`; a rejected registration threw a custom
`RegistrationException`; bad credentials threw with a *different message* depending on
whether the email was unknown or the password was wrong. Controllers then caught broadly
and returned `"An unexpected error occurred: " + ex.Message`.

Three separate problems came out of that:

- **Leakage.** Internal exception text — including the login endpoint's distinction
  between "no such email" and "wrong password", which turned it into a user directory —
  reached clients verbatim.
- **Inconsistency.** The same situation produced a 400 on one endpoint and a 500 on
  nineteen others, depending on whether that action happened to catch.
- **Invisibility.** A method signature returning `Task<TodoListDto>` says nothing about
  the two ways it can fail. You had to read the body.

## Decision

**Expected failures are values; only bugs are exceptions.**

| Situation | Mechanism |
|---|---|
| Not found, conflict, validation failure, unauthorised, rate-limited | `Result` / `Result<T>` carrying an `Error` |
| A domain invariant was violated | `throw new DomainException(...)` |
| Programming error, infrastructure failure | let it propagate |

`Error` is a record of `(Code, Message, ErrorType)`. `Code` is a stable dotted
identifier — `todoList.notFound`, `auth.login.invalidCredentials` — and it is the
contract: **clients branch on `code`, never on the prose in `detail`**. `ErrorType` says
how the transport should render the failure, and `ErrorMapping` is the single place that
turns it into an HTTP status and an RFC 7807 body.

Codes live in one file per vertical (`TodoListErrors`, `AuthErrors`) so that the same
situation cannot acquire two codes.

## Consequences

- The failure set is visible in the signature. `Task<Result<Guid>>` says "this can fail
  in a named way", and the caller cannot ignore it without ignoring `IsSuccess`.
- Uniformity is structural, not disciplinary. One mapping table means a validation
  failure is 400 everywhere, and there is no `try`/`catch` in any controller.
- Security-relevant answers can be made deliberately uninformative in one place:
  `auth.login.invalidCredentials` is returned for an unknown address, a wrong password,
  an unconfirmed email **and** a locked-out account, because saying which confirms the
  account exists. Likewise `todoList.notFound` covers "does not exist" and "belongs to
  somebody else", so 403-versus-404 cannot be used to enumerate other users' ids.
- Every use case signature carries `Result`, and callers must check `IsSuccess`. That is
  ceremony, and it is the point.
- `Result` is a class, so there is one allocation per call. Measured against a database
  round-trip this does not matter; if it ever did, a readonly struct is a drop-in change.
- `Result<T>.Value` throws when the result is a failure. That is intentional — reading
  the value of a failed result is a bug — but it means `IsSuccess` must be checked
  first, and nothing in the type system forces it. A full discriminated union would; C#
  does not have one.
- `DomainException` maps to 400 with a fixed message and is logged with its stack trace.
  Its text never reaches the client from the global handler.

## The nuance worth spelling out

`TodoListErrors.InvariantViolated(message)` exists so a use case that *expects* an
invariant to refuse can catch `DomainException` and turn it into a 409 `Result`, passing
the domain's own message through. That looks like it contradicts "invariant violations
are bugs" — it does not. The domain message is written by us, in terms of the user's own
data ("This list already contains an item titled 'Vacuum'"), so it is safe to return, and
passing it through means there is no second copy of the rule in the application layer
that could drift from the first. The alternative — duplicating "titles must be unique" as
an application-layer pre-check — is how two implementations of one rule diverge.

## Alternatives rejected

- **Exceptions for everything** (what was there). Control flow through exceptions, an
  unreadable failure surface, and leakage by default.
- **A `Result` library** (`FluentResults`, `ErrorOr`, `OneOf`, `LanguageExt`). More
  capable, and each brings its own vocabulary and a dependency in the layer that is
  supposed to have almost none. ~60 lines of `Result` plus ~40 of `Error` is small enough
  to own.
- **Nullable returns for "not found".** Works for exactly one failure mode and collapses
  the moment there are two.
- **Exceptions mapped centrally by type** (a `NotFoundException` → 404 filter). Solves
  consistency and leakage but not visibility: the signature still lies about what can
  happen, and cheap exceptions on a hot path are still not cheap.

## Revisit when

`Result`-checking boilerplate starts dominating use-case bodies — the answer then is
combinators (`Map`, `Bind`, `Tap`) on the existing type, not a different mechanism.
