# 0005 — Opaque rotating refresh tokens, not JWTs

Status: Accepted

## Context

The previous implementation minted the refresh token as a **JWT signed with the same key,
issuer, audience and claim set as the access token**, with a seven-day lifetime, and put
it in a cookie. Three consequences, all bad:

- A stolen refresh cookie *was itself a valid access token*, good for seven days on every
  authenticated endpoint — because the JWT bearer handler could not tell the two apart.
- Nothing was stored server-side, so nothing could be revoked. There was no logout.
- The refresh call was a body-less `POST /Authenticate` driven entirely by the cookie,
  with no antiforgery token — a CSRF-shaped endpoint that minted credentials.

## Decision

A refresh token is **32 bytes from a CSPRNG, base64url-encoded — not a JWT**. Only its
SHA-256 hash is persisted, in `identity.RefreshTokens`, under a unique index. It is
**returned in the response body**, not a cookie.

**Rotation on every use.** Presenting a token consumes it: the row is marked revoked, its
`ReplacedByTokenHash` points at the successor, and a new token is issued.

**Replay revokes the family.** Presenting a token that was already revoked means either
the legitimate holder replayed it or somebody else has a copy; both mean the chain can no
longer be trusted, so **every live token for that user is revoked** and the request fails.

`POST /api/v1/auth/logout` revokes the presented token. It is idempotent and silent about
whether the token existed.

Every rejection — unknown, expired, revoked, replayed — returns the same
`auth.refreshToken.invalid`.

## Consequences

- A stolen refresh token is not an access token. It is only usable against
  `/api/v1/auth/refresh`, and using it burns it.
- Theft becomes detectable and self-limiting: as soon as either party refreshes after the
  other, the replay revokes the whole family and both are logged out. Verified — replaying
  a consumed token returns 401, and the token it had been rotated into stops working too.
- Sessions can be ended. `RevokeAllForUserAsync` is both the theft response and the hook
  for "sign out everywhere".
- A database read and a write on every refresh. That is the cost of revocability, and it
  is why the access token stays short-lived (15 minutes by default) rather than the
  refresh path being called constantly.
- Only the hash is stored, so a database dump does not yield usable tokens. A plain
  SHA-256 is deliberate, not an oversight: unlike a password this is 256 bits of uniform
  random data, so there is nothing to brute-force and a slow KDF would only add latency
  to every refresh.
- Storing the token in the response body puts it in the client's hands. For a
  browser-only SPA an `HttpOnly; Secure; SameSite` cookie is the stronger choice against
  XSS; the controller documents where to make that switch, and nothing in Application
  depends on the answer. **This is a real trade-off, not a strict improvement**: body
  delivery removes the CSRF surface and works for mobile and service clients, at the cost
  of XSS exposure that a cookie would avoid.
- The `RefreshTokens` table grows. Revoked rows are kept, because
  `ReplacedByTokenHash` is what makes a replay detectable. A cleanup job for rows past
  their expiry is left to the reader — this template does not ship one.

## Alternatives rejected

- **JWT refresh tokens** (what was there). A refresh token must be revocable; a JWT's
  defining property is that it is verifiable without server state. Using the *same*
  signing key and audience as the access token additionally made the two
  interchangeable.
- **A separate key/audience for a JWT refresh token.** Fixes interchangeability, not
  revocability. There is still no way to end a session before expiry.
- **A long-lived access token and no refresh token.** Simplest, and the worst blast
  radius: a stolen token is valid until it expires and cannot be withdrawn.
- **A non-rotating stored refresh token.** Revocable, but a stolen copy stays valid for
  its whole lifetime and theft is undetectable. Rotation is what turns a leak into a
  signal.
- **Rotation without family revocation.** The replay is then a silent failure, and the
  attacker simply retries after the next legitimate refresh.
- **Reference tokens / full OAuth2 with an identity provider** (Duende, Keycloak, Entra
  ID). The correct answer for a real product with multiple clients, and far too much
  infrastructure for a template. What is here is the smallest correct thing;
  `IRefreshTokenGrants` and `IAccessTokenIssuer` are the seams if you outgrow it.

## Revisit when

You have more than one client type with different threat models, or you need token
introspection, consent or scopes — at which point adopt an identity provider rather than
extending this.
