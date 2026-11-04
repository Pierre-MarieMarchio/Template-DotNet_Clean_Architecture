# 0040. The signing key rotates through previous keys that only validate

Status: Accepted

## Context

Access tokens are HS256 JWTs signed with `Jwt:Key`, a secret every host that composes
authentication holds, since each one validates the tokens. A key must be replaceable: on a
schedule, when someone who held it leaves, or because it leaked. Replacing it at once refuses every
access token still in flight until its caller refreshes, and several instances that restart one at
a time disagree for a while on which key is current. The usual answers for a signing key that
rotates are a key id in each token's header, and a JWKS document the validating parties fetch, which
fit an asymmetric key published to third parties.

## Decision

- **One key signs, and a list of previous keys only validates.** A host signs with `Jwt:Key` alone,
  and accepts a token signed with `Jwt:Key` or with any key of `Jwt:PreviousKeys`. A rotation lists
  the old key there, waits out the access-token lifetime and its clock skew, then removes it; a
  leaked key never goes to the list.
- **No key id and no JWKS.** A token carries no `kid`, and validation tries each configured key; the
  keys come from configuration, as the current one does, and nothing is fetched.
- **A previous key is held to the current key's rules.** `JwtOptionsValidator` refuses, at start, a
  previous key shorter than 32 bytes, the development key outside Development, a previous key equal
  to `Jwt:Key` and a repeated one, naming `Jwt:PreviousKeys:<n>`.
- `ConfigureJwtBearerOptionsTests` holds that a previous key's token is accepted, that a key no
  longer listed is refused and that a new token is signed with `Jwt:Key`; `JwtOptionsValidatorTests`
  holds each refusal.

## Alternatives considered

- **Replacing the key in place.** Nothing to add, and every caller refused once, with a refresh to
  recover, on every rotation; and instances restarting one at a time refuse each other's tokens.
- **A `kid` header and a set of keys chosen by it.** Validation goes straight to one key, which
  matters with many keys or a key that cannot be tried cheaply; with HS256 and one or two keys, trying
  each costs nothing, and a `kid` would need naming, storing and keeping in step on every host.
- **Asymmetric keys published as JWKS.** The way to let parties that hold no secret validate tokens;
  every validating host here holds the secret already, and a JWKS endpoint is one more route, cache
  and key store to run.
- **A key store shared by the hosts, such as the data-protection key ring.** Rotation without a
  restart, at the cost of a store every host must reach before it can validate a token.

## Consequences

- A planned rotation signs no one out. A refresh token is a random value stored hashed, never
  signed with this key, so every session survives the rotation, as does any other credential the
  project issues that is not an access token.
- A rotation is a configuration change on every host and a restart, twice: once to list the old key,
  once to remove it. Several instances of one host need a step before, which lists the new key as a
  previous one everywhere first.
- A leaked key's tokens stop at once only because it is never listed: the rotation's safety rests on
  that rule, which no test can see.

[Rotating the signing key](../guides/rotating-the-signing-key.md) gives the steps.

## Revisit when

A party that holds no secret must validate the tokens, which calls for an asymmetric key and a
published key set, or the hosts can no longer be restarted for a rotation.
