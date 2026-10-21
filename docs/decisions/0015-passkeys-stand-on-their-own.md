# 0015. Passkeys stand on their own

Status: Accepted

## Context

A passkey sign-in is two calls: the server issues a challenge, the authenticator signs it. ASP.NET
Core Identity keeps the state between the two calls in a cookie, and this API sets no cookie for
sign-in and keeps no session between calls. A passkey also proves more than a password: possession
and, with user verification, the person. How it relates to the second factor, and what registering
or removing one does to open sessions, have to be decided.

## Decision

- **The client carries the ceremony state.** The adapter seals it with a time-limited data
  protector, under a purpose per ceremony kind and, for a registration, per account. It lives
  `Passkey:CeremonyLifetime`, five minutes by default, and is spent on its first presentation,
  before anything is verified: one insert keyed on its hash, so the database decides which of two
  presentations wins on any instance. A rewritten, expired, wrong-kind or spent state is refused
  like a ceremony that does not verify.
- **A user-verified passkey is a whole sign-in.** The ceremonies require user verification, not
  configurable, so possession and the user check happen together, the two factors a password and a
  code provide. A password sign-in still demands the armed second factor.
- **Registering one re-authenticates**, with the password when one is set and a recent access token
  otherwise, and ends no session. **Removing one** is treated as a password change: the stamp
  rotates and every other session ends ([ADR 0013](0013-a-session-is-a-refresh-token-family.md)).

## Consequences

- One captured state cannot sign in twice, even with an authenticator that keeps no signature
  counter, as synced passkeys do not.
- A stolen session cannot register its thief's passkey.
- `PasskeyCeremonyStateTests` refuses tampered, foreign, wrong-kind and spent states with a
  verifier that accepts everything, so only the state can be what refuses;
  `CompletePasskeySignInUseCaseTests` holds the sign-in rule.

[Passkeys](../features/authentication.md#passkeys) describes the endpoints and settings.

## Revisit when

Authenticators without user verification must be accepted, or the API gains a server-side session
store that could hold the state instead.
