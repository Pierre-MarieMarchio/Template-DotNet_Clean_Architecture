# Rotating the signing key

## When

The key that signs access tokens, `Jwt:Key`, is replaced: on a schedule, when someone who held it
leaves, or because it leaked. At the end, every new access token is signed with the new key, and no
signed-in caller was refused on the way.

An access token is an HS256 JWT signed with `Jwt:Key`. Every host that composes authentication
accepts a token signed with `Jwt:Key` or with one of `Jwt:PreviousKeys`, and signs only with
`Jwt:Key`; [the configuration reference](../reference/configuration.md#jwt) holds the rules each key
is held to, and [ADR 0040](../decisions/0040-the-signing-key-rotates-through-previous-keys-that-only-validate.md) says why the previous keys only validate.

| What a caller holds | What the rotation does to it |
|---|---|
| An access token | accepted until it expires while its key is listed in `Jwt:PreviousKeys`, refused once the key is gone |
| A refresh token | nothing: it is a random value stored hashed, never signed with the key, so every session survives ([ADR 0013](../decisions/0013-a-session-is-a-refresh-token-family.md)) |
| An access key, or a session cookie a server-rendered host keeps | nothing: an access key is a random value stored hashed, and a cookie is protected by its host's data-protection keys; neither is signed with this key |

A leaked key is the exception: tokens it signed must stop working at once, so it never goes to
`Jwt:PreviousKeys`. Set the new `Jwt:Key` alone; a caller refused with `401` refreshes, and gets a
token signed with the new key, since the leak exposed no refresh token.

## Steps

1. Generate the new key, 32 bytes at least: `openssl rand -base64 48`.
2. On every host that composes authentication, set `Jwt__Key` to the new key and
   `Jwt__PreviousKeys__0` to the old one, from the secret store the deployment reads, then restart
   the hosts: options are read once, at start. A host refuses to start when a previous key is shorter
   than 32 bytes, is the development key outside Development, equals `Jwt:Key` or repeats another
   one, and names `Jwt:PreviousKeys:<n>`.
3. Wait at least `Jwt:AccessTokenLifetimeInMinutes`, plus the 30 seconds of clock skew the
   validation allows, after the last host started signing with the new key: no token signed with
   the old key is still valid by then.
4. Remove `Jwt__PreviousKeys__0` and restart the hosts.

Several instances of one host that restart one at a time hold both configurations for a while, and an
instance still on the old key would refuse a token a restarted one signed. Run one more step before
step 2: set `Jwt__PreviousKeys__0` to the new key on every instance, keep `Jwt__Key` as it is, and
restart them all. Every instance then accepts the new key before any signs with it.

The local stack sets no previous key: for a rotation there, add `Jwt__PreviousKeys__0` beside
`Jwt__Key` under `x-host-environment` in `docker-compose.yml`, and remove it afterwards.

## Check

<!--#if (api)-->
An access token issued before step 2 still answers `200` on a protected route after step 2, until it
expires:

```bash
curl -i -H "Authorization: Bearer <token issued before step 2>" http://localhost:8080/api/v1/auth/me
```

<!--#endif-->
A host started with `Jwt__PreviousKeys__0` set to its own `Jwt__Key` stops before serving and names
`Jwt:PreviousKeys:0`. `ConfigureJwtBearerOptionsTests` holds that a previous key's token is accepted,
that a key no longer listed is refused and that a new token is signed with `Jwt:Key`;
`JwtOptionsValidatorTests` holds each refusal.

## Undo

The rotation runs the other way: set the old key back as `Jwt:Key`, list the new one in
`Jwt:PreviousKeys` until the tokens it signed have expired, then remove it.
