# Authentication

Who may call the API, and how a caller proves who it is.
<!--#if (accounts)-->
How an account lives, from registration to deletion, is in [Accounts](accounts.md).
<!--#endif-->

## The mode this project uses

<!--#if (localAuth)-->
This project keeps its own accounts, on ASP.NET Core Identity: a caller signs in with an email
address and a password, a passkey or a token from Google, Microsoft or Apple, and receives an
access token and a refresh token minted by this API.
<!--#endif-->
<!--#if (externalAuth)-->
This project keeps no passwords and mints no tokens: callers present access tokens issued by an
external OpenID Connect provider (Keycloak, Entra ID, Auth0…), and the API validates them.
<!--#endif-->
<!--#if (publicApi)-->
This project authenticates nobody: every endpoint answers any caller, and there is no account.
<!--#endif-->

The mode was chosen when the project was generated, and it decides which modules exist. Local
accounts hold the identity schema and the account routes; an external provider holds a mapping
from the provider's subjects to owner ids; a public API holds neither. Changing mode is a product
decision recorded against the baseline:
[ADR 0001](../decisions/0001-adopt-the-generated-baseline.md).

<!--#if (localAuth)-->
## Local accounts

### Signing in

`POST /api/v1/auth/login` takes an email and a password and answers in one of two shapes, told
apart by its `status` field:

```json
{ "status": "authenticated", "tokens": { "accessToken": "…", "accessTokenExpiresAt": "…", "refreshToken": "…", "refreshTokenExpiresAt": "…" } }
{ "status": "twoFactorRequired", "challengeToken": "…" }
```

A client reads `status` rather than guessing from which fields are present. Every refusal is the
same `401` `auth.login.invalidCredentials`: an unknown address, a wrong password, an unconfirmed
address and a locked account cost the same time and read the same, so the route reveals nothing
about who has an account ([ADR 0014](../decisions/0014-anonymous-routes-reveal-nothing.md)).

### Access and refresh tokens

The access token is a JWT signed with HS256, valid 15 minutes by default, its issuer, audience
and algorithm always checked with 30 seconds of clock skew. Each request also checks the account's
security stamp, so a password change or a lockout takes effect before the token expires.

The refresh token is an opaque 32-byte random value, of which only a SHA-256 hash is stored.
`POST /api/v1/auth/refresh` consumes it and returns a new pair. Presenting a token that was already
consumed is treated as theft: every session of the account is revoked.

### Sessions

One sign-in is one session: the family of refresh tokens that sign-in started. The access token
carries a `sid` claim naming it. `GET /api/v1/auth/sessions` lists the caller's live sessions,
`DELETE /api/v1/auth/sessions/{sessionId}` ends one, `logout` ends the current one and
`logout-all` ends them all.

A credential change (password, second factor, passkey removal) ends every other session; a
password reset through the mailed link ends them all
([ADR 0013](../decisions/0013-a-session-is-a-refresh-token-family.md)).

### Two-factor

An authenticator app (TOTP), on ASP.NET Core Identity's own primitives. `two-factor/setup`
provisions a key, `two-factor/confirm` arms it and returns ten single-use recovery codes, shown
once; arming and disarming both require the current password. With the factor armed, `login`
answers `twoFactorRequired`, and `login/two-factor` exchanges the challenge and a code for the
tokens. A challenge lives five minutes and dies after five wrong codes: that count, not the
lockout, bounds guessing.

### Passkeys

WebAuthn on ASP.NET Core Identity. Registering takes two calls (`passkeys/register/options`, then
`passkeys/register`) and re-authenticates the caller; signing in takes two anonymous calls
(`passkeys/sign-in/options`, then `passkeys/sign-in`). A user-verified passkey is a whole sign-in,
so it never asks for the second factor. The ceremony state travels to the client sealed, lives
five minutes and is spent once ([ADR 0015](../decisions/0015-passkeys-stand-on-their-own.md)).

<!--#if (accessKeys)-->
### Access keys

An access key stands for its owner where a token cannot be refreshed: at this project's MCP server,
whose clients keep one credential for weeks. `POST /api/v1/auth/access-keys`
creates one from a name, its scopes (`read`, `write` or both) and an optional expiry, and
re-authenticates the caller as registering a passkey does. The response carries the key, `ak_` and
43 characters, once: only its SHA-256 hash is stored. `GET /api/v1/auth/access-keys` lists the
live keys with their first characters and when each was last used, to the minute;
`DELETE /api/v1/auth/access-keys/{accessKeyId}` revokes one. An account holds at most 25 live keys;
a password change leaves them alive, a lockout refuses them and deleting the account deletes them.

This API never accepts an access key: its bearer scheme reads JWTs only. The MCP server validates
the key through `IAuthenticateAccessKeyUseCase` and acts as its owner. A malformed, unknown,
revoked or expired key, and one whose account may not sign in, all answer `auth.accessKey.invalid`
([ADR 0026](../decisions/0026-an-access-key-stands-for-its-owner-where-a-token-cannot-be-refreshed.md)).

<!--#endif-->
### Signing in with Google, Microsoft or Apple

The client runs the provider's authorisation flow with PKCE and posts the `id_token` to
`POST /api/v1/auth/login/external`. The API verifies it against the provider's published keys and
mints its own tokens, with the same `status` shapes as `login` plus `accountCreated`. One adapter
serves every OpenID Connect provider: adding one is an entry under `ExternalIdentity:Providers`,
with no secret. An identity is linked on (provider, subject); an existing account is linked
automatically only when both the provider and the account have proved the address. An armed second
factor still applies.

### Browsers: the refresh-token cookie

A client sending `X-Refresh-Token-Mode: cookie` on `login`, `login/two-factor`, `login/external`,
`passkeys/sign-in`, `refresh` and `logout` receives the refresh token as the `refresh_token`
cookie (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/v1/auth`) instead of in the body.
The cookie is read only beside that header, which is the defence against cross-site requests;
`refresh` and `logout` then take no body. Every local-accounts project answers the header, web app
or not, and a client that never sends it works as above ([ADR 0016](../decisions/0016-browser-refresh-token-in-a-cookie.md)).

<!--#if (anyWeb)-->
The single-page web apps of this project use this mode through the `api-client` package: the access
token stays in a variable, the cookie carries the refresh token, and one refresh runs at a time. A
server-rendered app uses neither: its server signs in in body mode, keeps both tokens, and gives the
browser a session cookie of its own
([ADR 0024](../decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md)). How an app
signs in is in [Web front end](web-frontend.md#signing-in).

<!--#endif-->
<!--#endif-->
<!--#if (externalAuth)-->
## An external identity provider

The API validates the provider's access tokens with the framework's JWT bearer handler: signature
against the authority's keys, issuer, audience, expiry, 30 seconds of skew. Nothing can be switched
off.

The provider's subject never becomes an owner id. The first request of each (issuer, subject)
creates a mapping to an internal `UserId`, exactly once even under concurrency, and every business
row carries that id ([ADR 0017](../decisions/0017-external-subjects-map-to-internal-owners.md)).
The mapping keeps the email and name the provider last sent, for notifications; an address the
provider marks unverified is not kept. A token that does not validate, or names no issuer or
subject, is `401` `auth.required`.

The client signs in with the provider, through a maintained OpenID Connect library running the
Authorization Code flow with PKCE, and sends its access token as `Authorization: Bearer`.
<!--#if (anyWeb)-->
A server-rendered web app runs that flow on its server, keeps the tokens there, and gives the
browser a session cookie of its own
([ADR 0024](../decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md)). How an app
signs in is in [Web front end](web-frontend.md#signing-in).
<!--#endif-->

<!--#endif-->
<!--#if (publicApi)-->
## A public API

The fallback authorisation policy admits every caller, and the pipeline runs no authentication.
An action marked `[Authorize]` stops the host at start-up, since nothing could satisfy it; an
operation that needs a caller fails, because `ICurrentUser` answers no one. Adding accounts later
means bringing an authentication module and its fallback policy back.

<!--#endif-->
<!--#if (accounts)-->
<!--#if (presentationBlazorServer && !publicApi)-->
## The presentation project

The presentation project signs people in to itself, with a cookie, over the same accounts: the
[presentation project](presentation.md#signing-in) describes its sign-in and its session.

<!--#endif-->
<!--#if (presentationMcp)-->
## The MCP server

The MCP server beside the API authenticates its callers itself and acts for the owner its
credential names: [presentation project](presentation.md#calling-it) describes the credential a
client sends and which tools it reaches
([ADR 0027](../decisions/0027-an-mcp-server-acts-for-the-caller-its-credential-names.md)).

<!--#endif-->
## Roles and policies

Authorisation is default-deny: every endpoint requires an authenticated caller unless it carries
`[AllowAnonymous]` ([Request pipeline](../architecture/request-pipeline.md#authorisation)). Ownership
is checked in each query, never by a policy
([ADR 0009](../decisions/0009-ownership-is-in-the-query.md)).

One policy, `Administrator`, guards the administration and maintenance routes.
<!--#if (localAuth)-->
It requires the `Admin` role, held in the identity schema and granted with
`PUT /api/v1/auth/accounts/{userId}/roles/Admin` by another administrator, or by the development
seed ([Accounts](accounts.md#development-seed)).
<!--#endif-->
<!--#if (externalAuth)-->
It requires the role named by `Authentication:External:Roles:AdministratorRole` (`Admin` by
default), read from a claim of the token (`Roles:Source` = `Claim`) or from the
`external_identity.RoleAssignments` table (`Local`, the default, written by SQL or a seed).
<!--#endif-->

A new policy is a constant beside `Administrator` in
`Src/Presentation/AppTemplate.Presentation.Core/Common/Security/AuthorizationPolicies.cs` and one
`AddPolicy` call where the module composes its authentication.

<!--#endif-->
## Configure

<!--#if (localAuth)-->
| Section | What it sets |
|---|---|
| [`Jwt`](../reference/configuration.md#jwt) | the signing key (a secret, 32 bytes at least), issuer, audience, access-token lifetime |
| [`RefreshToken`](../reference/configuration.md#refreshtoken) | refresh-token lifetime and how long expired rows are kept |
| [`Identity`](../reference/configuration.md#identity) | password policy and lockout |
| [`TwoFactor`](../reference/configuration.md#twofactor) | challenge lifetime, wrong codes allowed, recovery codes, the label an authenticator app shows |
| [`Passkey`](../reference/configuration.md#passkey) | the relying-party domain and the ceremony lifetime |
| [`ExternalIdentity`](../reference/configuration.md#externalidentity) | the providers whose `id_token` the API accepts |
| [`RateLimiting`](../reference/configuration.md#ratelimiting) | `AuthenticationPermitLimit`, the per-address budget of the sign-in routes |

The `Identity` section is the only password policy: Identity applies it at registration, change
and reset, and its refusals come back on the password field. The request validators check only
presence and a 256-character ceiling. The length has a floor of 8 that configuration cannot lower.

`Passkey:ServerDomain` is set once, before the first registration: changing it invalidates every
passkey.
<!--#endif-->
<!--#if (externalAuth)-->
| Section | What it sets |
|---|---|
| [`Authentication:External`](../reference/configuration.md#authenticationexternal) | the authority, the audience, the claims read and where roles come from |

Changing provider without losing owners means inserting, before the switch, one mapping row per
owner naming the new issuer and subject and the existing `UserId`.
<!--#endif-->
<!--#if (publicApi)-->
Nothing to configure.
<!--#endif-->

## Tests

<!--#if (localAuth)-->
The flows run against the project's real database in `Tests/Integration/AppTemplate.Api.IntegrationTests/Auth/`
(registration, two-factor, passkeys, password and email changes). The guarantees each have a test in
`Tests/Integration/AppTemplate.Api.IntegrationTests/Security/`: `LoginUniformityTests` (one answer,
one timing), `RefreshTokenTests` and `RefreshTokenCookieTests` (rotation, replay, the cookie),
`SecurityStampRotationTests`, `AccountLockoutTests`, `PasswordPolicyTests`,
`DefaultDenyAuthorizationTests` and `AdministratorPolicyTests`.
<!--#endif-->
<!--#if (externalAuth)-->
`Tests/Presentation/AppTemplate.Api.UnitTests/Features/ExternalIdentity/` covers validation, the
first-request mapping, role mapping and the settings; the integration suite signs its requests
with a test provider's keys.
<!--#endif-->
<!--#if (publicApi)-->
`ControllerContractTests` and the integration suite run without any authentication scheme.
`Program.cs` refuses `[Authorize]` at start-up; no test exercises that refusal.
<!--#endif-->

## Decisions

- [0001 Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): the authentication mode.
<!--#if (accounts)-->
- [0009 Ownership is in the query](../decisions/0009-ownership-is-in-the-query.md)
<!--#endif-->
<!--#if (localAuth)-->
- [0013 A session is a refresh-token family](../decisions/0013-a-session-is-a-refresh-token-family.md)
- [0014 Anonymous routes reveal nothing](../decisions/0014-anonymous-routes-reveal-nothing.md)
- [0015 Passkeys stand on their own](../decisions/0015-passkeys-stand-on-their-own.md)
- [0016 Browser refresh token in a cookie](../decisions/0016-browser-refresh-token-in-a-cookie.md)
<!--#if (accessKeys)-->
- [0026 An access key stands for its owner where a token cannot be refreshed](../decisions/0026-an-access-key-stands-for-its-owner-where-a-token-cannot-be-refreshed.md)
<!--#endif-->
<!--#endif-->
<!--#if (externalAuth)-->
- [0017 External subjects map to internal owners](../decisions/0017-external-subjects-map-to-internal-owners.md)
<!--#endif-->
