# 0013. A session is a refresh-token family

Status: Accepted

## Context

A sign-in issues a short-lived access token and an opaque refresh token that is rotated on every
use. Rotation alone cannot answer what an account holder asks: which devices are signed in, how to
sign one out, and what a new password does to the others. ASP.NET Core Identity's security stamp
fails every access token at its next validation, but refresh tokens are this project's own and
survive a stamp rotation.

## Decision

- **One sign-in starts one family of refresh tokens, and the family is the session.** Its id is the
  `sid` claim of every access token issued in it. The owner lists sessions and ends one; a session
  records its client's `User-Agent` at last use and no address.
- **A spent token presented again ends every session of the account**, since a replay means the
  chain was copied.
- **Every stamp rotation revokes refresh tokens, through one policy.** Identity leaves the stamp
  alone on some changes that take access away, so the adapters rotate it themselves, and
  `CredentialInvalidationPolicy` then revokes the grants.
- **Which sessions end depends on where the change came from.** A password change, arming or
  dropping two-factor, or removing a passkey, made from a session, ends every other session and
  spares that one, which has just proved the new posture. A password reset through a mailed link,
  made from no session, ends them all, as does an administrator's lock or role change. A token that
  names no session spares nothing.
- **Ending a session is two conditional statements**, the family then its live grants, never a loop
  over grants: the replay path is one an attacker can trigger at will.

## Consequences

- An account holder can see and end sessions, and a credential change behaves the same whichever
  use case makes it. A new operation that rotates the stamp calls the policy.
- "Sign out everywhere" revokes every grant and rotates nothing, so the access token that asked
  lives out its few minutes.
- `SecurityStampRotationTests` and `RefreshTokenTests` replay the token in circulation after each
  change; `RefreshTokenRotationTests` proves on a real database that one of two simultaneous
  rotations wins.

[Sessions](../features/authentication.md#sessions) describes the endpoints.

## Revisit when

Access tokens must end the instant a session does, which needs a server-side check on every
request, or sessions must be shared across several APIs.
