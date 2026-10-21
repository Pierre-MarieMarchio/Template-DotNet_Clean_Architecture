# Accounts

How an account lives in this project, from its creation to its deletion. How a caller signs in is
in [Authentication](authentication.md); every route named here is in the
[API reference](../reference/api.md).

<!--#if (localAuth)-->
## Registration and email confirmation

The email address is an account's only identifier: there is no user name, and Identity's
`UserName` is kept equal to the address. A unique index on the normalised address holds it.

`POST /api/v1/auth/register` takes `email` and `password` and always answers `202`
`{"status":"pending"}`. The address learns by mail what happened: a confirmation link for a new
account, a fresh link for one still unconfirmed, a notice with sign-in and reset hints for one
already confirmed. A `400` only ever concerns the submitted values (address format, password
policy), never whether the address is taken
([ADR 0014](../decisions/0014-anonymous-routes-reveal-nothing.md)).

An account signs in only once its address is confirmed. The link's page posts the address and the
token to `POST /api/v1/auth/confirm-email`; `resend-confirmation-email` mails a fresh link and
answers `204` whether or not the address exists. A confirmation token lives one day
(`IdentityTokens:Lifespan`), since it waits for someone to open their mail.

## Password reset and email change

| Action | Route | What it takes | What it ends |
|---|---|---|---|
| Ask for a reset | `POST /api/v1/auth/forgot-password` | the address; `204` whether or not it exists | nothing |
| Reset | `POST /api/v1/auth/reset-password` | address, token, new password | every session |
| Change the password | `POST /api/v1/auth/change-password` | current and new password | every other session |
| Ask to change the address | `POST /api/v1/auth/change-email` | current password, new address | nothing |
| Confirm the new address | `POST /api/v1/auth/confirm-email-change` | new address, token; authenticated | nothing |

Each mailed token has its own provider and lifespan: one hour for a reset
(`PasswordReset:TokenLifespan`) and for an address change (`EmailChange:TokenLifespan`), so one
purpose's token never opens another. An address change naming an address another account holds
answers `204` as well, and mails that address a notice instead of a link.

## The pages the mailed links open

Three mails carry a link to a page, which reads it and posts it back to the API. Whatever serves
those pages implements this contract:

- the link is the configured URL followed by `#email=<address>&token=<token>`, both values
  percent-encoded;
- the page reads the fragment with `URLSearchParams` over `location.hash` and drops it from the
  address bar; a fragment never reaches a server, so the token stays out of access logs and
  `Referer` headers;
- the page posts JSON, never a GET with a query string.

| Setting | Default route | The page then posts |
|---|---|---|
| `EmailConfirmation:ConfirmEmailUrl` | `/confirm-email` | `{ "email", "token" }` to `/api/v1/auth/confirm-email`, anonymously; `204`, or `400` `auth.confirmEmail.invalid` |
| `PasswordReset:ResetPasswordUrl` | `/reset-password` | `{ "email", "token", "newPassword" }` to `/api/v1/auth/reset-password`, anonymously, after asking for the new password; `204`, or `400` `auth.resetPassword.invalid` or a password-policy refusal |
| `EmailChange:ConfirmEmailChangeUrl` | `/confirm-email-change` | `{ "newEmail", "token" }` to `/api/v1/auth/confirm-email-change`, the fragment's `email` being the new address; authenticated, so the page signs its holder in first; `204`, or `400` `auth.changeEmail.invalid` |

The three settings are required, absolute and without a fragment, and must be reachable from a
browser. Their defaults name the API's own origin (`http://localhost:5187` in
`appsettings.Development.json`, `http://localhost:8080` under Compose through `CONFIRM_EMAIL_URL`,
`RESET_PASSWORD_URL` and `CONFIRM_EMAIL_CHANGE_URL`), where no page answers: the links stay there
until a front end has the three pages. Then point the three settings at that front end's origin.
Route names are free: the settings take any absolute page URL.

<!--#if (anyWeb)-->
For a web app of this project, the steps are in
[Web front end](web-frontend.md#the-pages-the-mailed-links-open).

<!--#endif-->
<!--#endif-->
## Administration

<!--#if (localAuth)-->
An administrator (the `Administrator` policy, held by the `Admin` role) works on other accounts
under `/api/v1/auth/accounts`: list and filter them by address, lock one out or lift the lockout,
grant or revoke a role, disarm a second factor whose holder lost both the app and the recovery
codes, read an account's security log, delete it. Locking out, a role change and disarming rotate
the account's security stamp, so its tokens stop working at once.

No write may target the caller's own account: each refuses with `403` and a
`*.cannotTargetSelf` code, so a stolen administrator session cannot lock out, demote or strip the
account it runs as. The two reads include the caller.
<!--#endif-->
<!--#if (externalAuth)-->
The provider holds the accounts, their credentials and their administration. This project holds
the mapping from the provider's subjects to owner ids and, when roles are local, the
`external_identity.RoleAssignments` table, written by SQL or a seed: there is no route for roles.

An administrator deletes an account's local presence with
`DELETE /api/v1/external-identities/{userId}`, which removes every mapping naming the owner and
its role assignments. No security log is kept in this mode: the provider's own logs are the trail.
<!--#endif-->

## Deleting an account

<!--#if (localAuth)-->
A holder deletes their own account with `POST /api/v1/auth/delete-account`, which demands proof on
the way out: the current password when the account has one, a two-factor code when the factor is
armed, otherwise an access token issued less than five minutes ago (`403`
`auth.account.reAuthenticationRequired` when the token is older). An administrator deletes any
other account with `DELETE /api/v1/auth/accounts/{userId}`. Either way the account and its sessions
go at once.
<!--#endif-->
<!--#if (externalAuth)-->
Deleting the mapping is deleting the account here: a caller who signs in again is provisioned
under a new owner id, with none of the deleted one's data. Wiring the provider's own deletion
signal to the same service is left to the project.
<!--#endif-->

Once the deletion has committed, `UserAccountDeleted` is published.
<!--#if (anyFeature)-->
Each feature that owns user data decides the fate of its rows, delete or anonymise, and applies it
twice: a consumer of the fact is the fast path, and a leased sweep in the maintenance job
(`Jobs:Maintenance:SweepDeletedAccountsDataEnabled`) is the guarantee, since in-process
publication is at most once. Both check that the account is really gone before writing, and both
are idempotent ([ADR 0012](../decisions/0012-account-deletion-each-feature-decides.md)). Every
feature this project ships deletes. A generated feature states its choice with
`new-feature --deletion delete|anonymise` ([Adding a feature](../guides/adding-a-feature.md)).
<!--#endif-->
<!--#if (!anyFeature)-->
No feature reacts to it yet: the first feature that owns user data brings the consumer, the
policy and the leased sweep, written by `new-feature --deletion delete|anonymise`
([Adding a feature](../guides/adding-a-feature.md)).
<!--#endif-->

<!--#if (localAuth)-->
## The security log

Every sign-in attempt, lockout, logout, refresh-token revocation or replay, stamp rotation,
second-factor and passkey change, role change and deletion is recorded with its time, the client
address and the user agent. The holder reads theirs with `GET /api/v1/auth/me/security-events`, an
administrator any account's with `GET /api/v1/auth/accounts/{userId}/security-events`.

The log survives the account it describes, so an investigation after a deletion still has it, and
it is purged after `SecurityEventLog:RetentionInDays` (90 days by default) by the maintenance job,
or by `DELETE /api/v1/maintenance/security-events/expired`. An event never names an email address.

## Development seed

`IdentitySeed` creates an administrator with a confirmed address at start-up, in Development only:

```json
"IdentitySeed": { "Enabled": true, "AdminEmail": "administrator@localhost" }
```

Set `IdentitySeed:AdminPassword` with `dotnet user-secrets`; a blank password fails start-up, and
`Enabled` outside Development throws rather than seeding.

<!--#endif-->
## Decisions

- [0009 Ownership is in the query](../decisions/0009-ownership-is-in-the-query.md)
<!--#if (anyFeature)-->
- [0012 Account deletion: each feature decides](../decisions/0012-account-deletion-each-feature-decides.md)
<!--#endif-->
<!--#if (localAuth)-->
- [0013 A session is a refresh-token family](../decisions/0013-a-session-is-a-refresh-token-family.md)
- [0014 Anonymous routes reveal nothing](../decisions/0014-anonymous-routes-reveal-nothing.md)
<!--#endif-->
<!--#if (externalAuth)-->
- [0017 External subjects map to internal owners](../decisions/0017-external-subjects-map-to-internal-owners.md)
<!--#endif-->
