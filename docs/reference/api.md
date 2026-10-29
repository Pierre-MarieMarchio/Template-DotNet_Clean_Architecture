# API reference

Every route and every refusal `code`. The rules all endpoints follow are in
[HTTP API](../architecture/http-api.md). In Development the OpenAPI document is at
`/openapi/v1.json`, Scalar at `/scalar/v1`.
<!--#if (accounts)-->
A route requires a signed-in caller unless marked anonymous; another caller's resource is `404`.
<!--#endif-->
<!--#if (publicApi)-->
This project authenticates nobody: every route answers any caller.
<!--#endif-->

## Routes

### Health

| Method | Route | Success | Notes |
|---|---|---|---|
| GET | `/health` | 200 | Anonymous. Liveness: checks nothing. |
| GET | `/health/ready` | 200 | Anonymous. Readiness: the database and the shutdown state; `503` otherwise. |

<!--#if (localAuth)-->
### Authentication

Under `/api/v1/auth`; † marks the routes under `RateLimiting:AuthenticationPermitLimit`.

| Method | Route | Success | Notes |
|---|---|---|---|
| POST | `/register` † | 202 | Anonymous. Always `{"status":"pending"}`; the address learns by mail what happened. |
| POST | `/confirm-email` † | 204 | Anonymous. `email` and `token` from the mailed link. |
| POST | `/resend-confirmation-email` † | 204 | Anonymous. Same answer whether or not the address exists. |
| POST | `/login` † | 200 | Anonymous. `status` is `authenticated` (with `tokens`) or `twoFactorRequired` (with `challengeToken`). |
| POST | `/login/two-factor` † | 200 | Anonymous. The challenge and a code or recovery code; answers as `login`. |
| POST | `/login/external` † | 200 | Anonymous. `provider` and `idToken`; answers as `login`, plus `accountCreated`. |
| POST | `/passkeys/sign-in/options` † | 200 | Anonymous. Options for `navigator.credentials.get()` and the sealed `authenticationState`. |
| POST | `/passkeys/sign-in` † | 200 | Anonymous. The assertion and the state; always `authenticated`. |
| POST | `/refresh` † | 200 | Anonymous. Consumes the refresh token, returns a new pair. |
| POST | `/logout` † | 204 | Anonymous. Ends the refresh token's session; idempotent. |
| POST | `/forgot-password` † | 204 | Anonymous. Same answer whether or not the address exists. |
| POST | `/reset-password` † | 204 | Anonymous. `email`, `token`, `newPassword`; ends every session. |
| POST | `/logout-all` | 204 | Ends every session of the caller. |
| GET | `/sessions` | 200 | The caller's live sessions, the current one marked. |
| DELETE | `/sessions/{sessionId}` | 204 | Ends one of the caller's sessions. |
| GET, HEAD | `/me` | 200 | The caller's profile and roles. |
| GET | `/me/security-events` | 200 | The caller's security log, newest first, offset-paged. |
| POST | `/change-password` † | 204 | Current and new password; ends every other session. |
| POST | `/change-email` † | 204 | Current password and new address; mails the new address. |
| POST | `/confirm-email-change` † | 204 | `newEmail` and `token` from the mailed link. |
| POST | `/delete-account` † | 204 | Re-authenticates: password, two-factor code, or an access token under five minutes old. |
| POST | `/two-factor/setup` † | 200 | Provisions a key; rotates the security stamp, so sign in again before confirming. |
| POST | `/two-factor/confirm` † | 200 | Current password and a code; arms the factor, returns ten recovery codes, ends every other session. |
| POST | `/two-factor/disable` † | 204 | Current password; ends every other session. |
| POST | `/passkeys/register/options` † | 200 | Options for `navigator.credentials.create()` and the sealed `registrationState`. |
| POST | `/passkeys/register` † | 200 | The credential, the state, an optional name; re-authenticates. |
| GET | `/passkeys` | 200 | The caller's passkeys, newest first. |
| DELETE | `/passkeys/{credentialId}` | 204 | Removes one; ends every other session. |
<!--#if (accessKeys)-->
| POST | `/access-keys` † | 201 | A name, `read` and/or `write` scopes, an optional `expiresAt` and the current password; the only response that carries the key. |
| GET | `/access-keys` | 200 | The caller's live access keys, newest first, without their secrets. |
| DELETE | `/access-keys/{accessKeyId}` | 204 | Revokes one of the caller's access keys. |
<!--#endif-->

The sign-in routes, `refresh` and `logout` accept `X-Refresh-Token-Mode: cookie`
([cookie mode](../features/authentication.md#browsers-the-refresh-token-cookie)).

### Account administration

Under `/api/v1/auth/accounts`, `Administrator` policy; a write on one's own account is `403`.

| Method | Route | Success | Notes |
|---|---|---|---|
| GET | `/` | 200 | Accounts by address, offset-paged; `email` filters by prefix. |
| POST | `/{userId}/lockout` | 204 | Locks the account out indefinitely. |
| DELETE | `/{userId}/lockout` | 204 | Lifts the lockout. |
| PUT | `/{userId}/roles/{role}` | 204 | Grants a role (`Admin`); `400` if unknown or already held. |
| DELETE | `/{userId}/roles/{role}` | 204 | Revokes a role; `400` if unknown or not held. |
| DELETE | `/{userId}/two-factor` | 204 | Disarms the second factor. |
| DELETE | `/{userId}` | 204 | Deletes the account. |
| GET | `/{userId}/security-events` | 200 | That account's security log; an empty page, never `404`, since the log outlives the account. |

<!--#endif-->
<!--#if (externalAuth)-->
### External identities

| Method | Route | Success | Notes |
|---|---|---|---|
| DELETE | `/api/v1/external-identities/{userId}` | 204 | `Administrator` policy. Deletes every mapping naming the owner and its role assignments; `404` when none does. |

<!--#endif-->
<!--#if (todoLists)-->
### To-do lists

Under `/api/v1/todo-lists`; items are reached through their list, and writes answer the list.

| Method | Route | Success | Notes |
|---|---|---|---|
| GET | `/` | 200 | The caller's lists, paged; sortable by `name`, `createdAt`, `lastModifiedAt`; `search` on the name. |
| GET, HEAD | `/{todoListId}` | 200 | The list with its items and tags. |
| POST | `/` | 201 | `Idempotency-Key` accepted. |
| PUT | `/{todoListId}` | 200 | Renames. |
| DELETE | `/{todoListId}` | 204 | |
| GET, HEAD | `/{todoListId}/items` | 200 | |
| GET, HEAD | `/{todoListId}/items/{todoItemId}` | 200 | |
| POST | `/{todoListId}/items` | 201 | `Idempotency-Key` accepted. |
| PUT | `/{todoListId}/items/{todoItemId}` | 200 | Replaces title and description. |
| POST | `/{todoListId}/items/{todoItemId}/complete` | 200 | |
| POST | `/{todoListId}/items/{todoItemId}/reopen` | 200 | |
| DELETE | `/{todoListId}/items/{todoItemId}` | 200 | Answers with the list. |
| POST | `/{todoListId}/items/{todoItemId}/tags` | 200 | Adds tags. |
| PUT | `/{todoListId}/items/{todoItemId}/tags` | 200 | Replaces the tag set. |
| DELETE | `/{todoListId}/items/{todoItemId}/tags/{tag}` | 200 | |
| GET | `/tags` | 200 | The tags the caller used, through a one-minute cache. |

<!--#endif-->
<!--#if (reminders)-->
### Reminders

| Method | Route | Success | Notes |
|---|---|---|---|
| GET | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reminders` | 200 | The caller's reminders for that item, cancelled ones included. An unknown or foreign item answers an empty list, not `404`, since a reminder outlives its item. |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reminders` | 201 | `Location` names the list above. `Idempotency-Key` accepted. |
| PUT | `/api/v1/reminders/{reminderId}` | 200 | Reschedules. |
| DELETE | `/api/v1/reminders/{reminderId}` | 204 | Cancels. |

<!--#endif-->
<!--#if (files)-->
### Files

Under `/api/v1/files`; the flows are in [Files](../features/files.md).

| Method | Route | Success | Notes |
|---|---|---|---|
| GET | `/` | 200 | The caller's files, paged; sortable by `name`, `registeredAt`, `availableAt` (offset only); `search` on the name, `state` filter. |
| GET, HEAD | `/{fileId}` | 200 | Metadata and `ETag`; `304` to a matching `If-None-Match`. |
| GET | `/{fileId}/content` | 302 | To a signed URL, `no-store`; `200` with the bytes on the `FileSystem` store. |
| POST | `/` | 201 | Registers a direct upload, returns the grant. `Idempotency-Key` accepted. |
| POST | `/uploads` | 201 | `multipart/form-data`; registers, stores and confirms. Not idempotent. |
| POST | `/{fileId}/confirm` | 200 | Checks the deposit; the file becomes `deposited`. |
| PUT | `/{fileId}/tags` | 200 | Replaces the tag set; honours `If-Match`. |
| GET | `/tags` | 200 | The tags the caller used, through a one-minute cache. |
| DELETE | `/{fileId}` | 204 | Honours `If-Match`. |

<!--#endif-->
<!--#if (notes)-->
### Notes

Under `/api/v1/notes`.

| Method | Route | Success | Notes |
|---|---|---|---|
| GET | `/` | 200 | Summaries of the caller's notes, paged; sortable by `title`, `createdAt`. |
| GET, HEAD | `/{noteId}` | 200 | The note and its `ETag`; `304` to a matching `If-None-Match`. |
| POST | `/` | 201 | Title up to 200 characters, body up to 10 000. `Idempotency-Key` accepted. |
| PUT | `/{noteId}` | 200 | Replaces title and body; honours `If-Match`. |
| DELETE | `/{noteId}` | 204 | Honours `If-Match`. |

<!--#endif-->
<!--#if (accounts)-->
### Maintenance

Under `/api/v1/maintenance`, `Administrator` policy; each answers `200` and the rows removed.

| Method | Route | Removes |
|---|---|---|
| DELETE | `/idempotency-keys/expired` | idempotency keys past `Idempotency:Retention` |
<!--#if (localAuth)-->
| DELETE | `/refresh-tokens/expired` | refresh-token rows past `RefreshToken:RetentionInDays` |
| DELETE | `/security-events/expired` | security events past `SecurityEventLog:RetentionInDays` |
<!--#endif-->

<!--#endif-->
## Error codes

Branch on a refusal's `code`, never on its `detail`.

### Authentication and authorisation

<!--#if (publicApi)-->
This project authenticates nobody, so no route answers these two; they are what a `401` or a `403`
carries once one does.

<!--#endif-->
| `code` | Status | When |
|---|---|---|
<!--#if (accounts)-->
| `auth.required` | 401 | No valid token, a route that matched nothing, or an account deleted since |
| `auth.forbidden` | 403 | Authenticated, but the endpoint's policy is not satisfied |
<!--#endif-->
<!--#if (publicApi)-->
| `auth.required` | 401 | A route that requires a caller, called without one |
| `auth.forbidden` | 403 | A caller the endpoint's policy refuses |
<!--#endif-->
<!--#if (localAuth)-->
| `auth.login.invalidCredentials` | 401 | One answer for an unknown address, a wrong password, an unconfirmed address and a lockout |
| `auth.login.invalidTwoFactorChallenge` | 401 | The challenge is unknown, spent, expired or out of attempts |
| `auth.refreshToken.invalid` | 401 | Unknown, expired, revoked or replayed; in cookie mode, no cookie |
| `auth.externalSignIn.refused` | 401 | An unknown provider or a token that did not verify |
| `auth.passkeySignIn.invalid` | 401 | Any passkey sign-in refusal |
| `auth.confirmEmail.invalid`, `auth.resetPassword.invalid`, `auth.changeEmail.invalid` | 400 | The token is unknown, spent or expired |
<!--#if (accessKeys)-->
| `auth.account.reAuthenticationRequired` | 403 | Deleting the account, registering a passkey or creating an access key needs a proof, or an access token under five minutes old |
<!--#endif-->
<!--#if (!accessKeys)-->
| `auth.account.reAuthenticationRequired` | 403 | Deleting the account or registering a passkey needs a proof, or an access token under five minutes old |
<!--#endif-->
| `auth.account.notFound` | 404 | An administration action naming no account |
| `auth.session.notFound` | 404 | A session that is not the caller's |
| `auth.passkey.notFound` | 404 | A passkey that is not the caller's |
| `auth.passkey.registrationRejected` | 400 | The ceremony verified, but the store refused the credential |
<!--#if (accessKeys)-->
| `auth.accessKey.notFound` | 404 | An access key that is not the caller's, or is already revoked or expired |
| `auth.accessKey.limitReached` | 409 | Creating an access key while the account holds 25 live ones |
| `auth.accessKey.invalid` | 401 | Answered by a host that accepts access keys, never by this API: a malformed, unknown, revoked or expired key, or one whose account may not sign in |
<!--#endif-->
| `auth.twoFactor.alreadyEnabled` | 409 | Setting up or confirming a factor already armed |
| `auth.account.cannotDeleteSelf`, `auth.lockout.cannotTargetSelf`, `auth.roles.cannotTargetSelf`, `auth.twoFactor.cannotTargetSelf` | 403 | An administrator aiming at their own account |
| `auth.account.deletionRejected`, `auth.lockout.rejected`, `auth.twoFactor.setupRejected`, `auth.twoFactor.enableRejected`, `auth.twoFactor.disableRejected`, `auth.twoFactor.administrativeDisableRejected` | 400 | The store refused the change; nothing changed |
<!--#endif-->
<!--#if (externalAuth)-->
| `externalIdentity.mapping.notFound` | 404 | No mapping names the owner id |
<!--#endif-->

### Resources

| `code` | Status | When |
|---|---|---|
<!--#if (todoLists)-->
| `todoList.notFound`, `todoItem.notFound` | 404 | Missing, or another caller's |
<!--#endif-->
<!--#if (reminders)-->
| `reminder.notFound` | 404 | Missing, or another caller's |
| `reminder.targetNotFound` | 404 | The item is missing, or another caller's |
<!--#endif-->
<!--#if (files)-->
| `storedFile.notFound` | 404 | Missing, or another caller's, except an available file of an `Authenticated` category |
| `storedFile.notAvailable` | 409 | No verdict yet, or the store no longer holds the content |
| `storedFile.quarantined` | 409 | Inspected and refused; never becomes available |
| `storedFile.depositMissing` | 409 | `confirm` found nothing deposited |
| `storedFile.quotaExceeded` | 409 | The owner's file, byte or pending allowance in the category |
| `storedFile.quotaBusy` | 409 | The quota lock was held more than 10 seconds; nothing written, retry |
| `storedFile.directUploadUnavailable` | 409 | The store cannot sign, or the category allows uploads through the API only |
| `storedFile.apiUploadUnavailable` | 409 | The category allows direct uploads only |
<!--#endif-->
<!--#if (notes)-->
| `note.notFound` | 404 | Missing, or another caller's |
<!--#endif-->
| `domain.invariantViolated` | 409 | A domain rule refused the change |
| `concurrency.conflict` | 409 | The row changed between the write's read and its commit; reload and retry |

### The request itself

| `code` | Status | When |
|---|---|---|
| `request.validationFailed` | 400 | A body, route or query value refused on the way in; the fields are in `errors` |
| `request.malformed` | 400 | A framework or middleware refusal with no more specific code |
| `paging.invalid`, `sort.invalid`, `filter.invalid`, `cursor.invalid` | 400 | One bound of the collection contract |
| `precondition.malformed` | 400 | `If-Match` is neither `*` nor a list of quoted entity tags |
| `precondition.failed` | 412 | The `If-Match` is stale, or `*` against a missing resource |
| `precondition.required` | 428 | `Concurrency:IfMatch` is `Required` and no `If-Match` was sent |
| `idempotency.keyInvalid` | 400 | The key is blank or too long |
| `idempotency.callerNotIdentifiable` | 400 | A key on a request with no identifiable caller |
| `idempotency.keyReused` | 409 | The same key with a different body |
| `idempotency.inProgress` | 409 | The first request under this key has not finished |
| `idempotency.notReplayable` | 409 | The stored response was too large to replay |
| `route.notFound` | 404 | No route matched |
| `request.methodNotAllowed` | 405 | |
| `request.notAcceptable` | 406 | |
| `request.tooLarge` | 413 | Over `RequestLimits:MaxRequestBodyBytes` |
| `request.unsupportedMediaType` | 415 | |
| `rateLimit.exceeded` | 429 | With `Retry-After` |
| `request.timeout` | 504 | Over `RequestTimeouts:Default` before the response started |
| `request.cancelled` | 499 | The client went away |
| `server.unexpected` | 500 | No exception text; the `traceId` joins the log |
| `request.failed` | any | A status no other code names |

A passkey registration that does not verify is `request.validationFailed` on `credential`.
<!--#if (accounts)-->
An anonymous caller on an unknown route gets `401` `auth.required`, not `404`.
<!--#endif-->
<!--#if (files)-->
On `POST /api/v1/files/uploads`, `request.tooLarge` applies above `Files:ApiUploadMaxBytes` plus
16 KiB.
<!--#endif-->
