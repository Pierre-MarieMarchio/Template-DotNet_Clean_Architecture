# 0026. An access key stands for its owner where a token cannot be refreshed

Status: Accepted

## Context

A sign-in yields an access token that lives 15 minutes and a refresh token that is rotated on every
use ([ADR 0013](0013-a-session-is-a-refresh-token-family.md)). A client that keeps one credential
in its configuration for weeks cannot hold either: a client of this project's MCP server takes a
fixed header and never refreshes it. Such a host needs a
credential that outlives a session, that the account holder can see and end, and whose theft costs
less than the theft of the password.

## Decision

- **An access key is a random secret the signed-in owner creates, lists and revokes.** It is `ak_`
  followed by 32 random bytes in base64url; only its SHA-256 hash is stored, under a unique index,
  and it is looked up by that hash. The creation response is the only one that carries it; the list
  shows a prefix of 11 characters, the scopes, the creation, last use and expiry, never the secret.
- **It is scoped and may expire.** `read`, `write` or both, at least one; an expiry in the future
  or none. A revoked or expired key is gone from the list and refused, and an account holds at most
  25 live keys.
- **Creating one re-authenticates**, as registering a passkey does: the current password when the
  account has one, an access token under five minutes old otherwise
  ([ADR 0015](0015-passkeys-stand-on-their-own.md)). A stolen 15-minute token cannot mint a
  long-lived credential.
- **This API never accepts one.** Its bearer scheme reads JWTs only. The MCP server, whose callers
  cannot refresh a token, validates a key through `IAuthenticateAccessKeyUseCase` and acts as the
  owner it names
  ([ADR 0027](0027-an-mcp-server-acts-for-the-caller-its-credential-names.md)), so the owner stays in every query
  ([ADR 0009](0009-ownership-is-in-the-query.md)). A malformed, unknown, revoked or expired key, and
  a key whose account the refresh path would refuse (locked out, deleted, or unconfirmed where
  confirmation is required), all answer
  the same `auth.accessKey.invalid`. A use records the time at most once a minute, in one
  conditional update.

## Alternatives considered

- **Longer-lived access tokens**: a JWT cannot be revoked before it expires, and lengthening every
  token to serve a few clients weakens all of them.
- **Refreshing in the client**: the clients in question take a static header; those that could
  refresh would each carry rotation and replay handling this project cannot test.
- **An OAuth authorisation server with the device flow**: the standard answer, and the right one
  once third parties act for users, but a whole server and its consent screens for what is still
  the owner's own credential.
- **Personal access tokens accepted by this API**: every endpoint would gain a second credential
  that skips the security stamp and the sessions, and a key leaked from a tool would reach the
  account routes.
- **Access keys in every project with local accounts**: routes and a table that nothing accepts
  until a host does; they come with the MCP server instead.

## Consequences

- An owner can give a tool a credential and take it back without touching the password, and sees
  when each key was last used.
- A password change, a reset or a stamp rotation leaves keys alive: revoking them is the owner's
  act, from the list. Locking or deleting the account refuses or deletes them.
- The count limit is checked before the insert, so two simultaneous creations may pass it by one.
- `CreateAccessKeyUseCaseTests` holds the re-authentication, the limit and the shown-once outcome;
  `AuthenticateAccessKeyUseCaseTests` the single refusal and the throttle;
  `AccessKeysServicePersistenceTests` the stored hash, the owner's isolation and liveness on a real
  database; `AccessKeyManagementTests` the routes, another caller's key answering `404`, and this
  API refusing a key as a bearer token.

[Access keys](../features/authentication.md#access-keys) describes the endpoints.

## Revisit when

Third parties act on an owner's behalf and need consent and delegation, a key must carry finer
permissions than `read` and `write`, or a second host must accept keys.
