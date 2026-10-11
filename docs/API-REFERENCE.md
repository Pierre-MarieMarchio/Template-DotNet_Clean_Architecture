# API reference

The endpoints, one section per feature. What holds for all of them — versioning, default-deny
authorisation, the `code` a failure carries, `ETag`, `Idempotency-Key`, the collection contract and
the rate limits — is in [`API-CONVENTIONS.md`](API-CONVENTIONS.md). Read that first; nothing here
repeats it.

[`AppTemplate.Api.http`](../AppTemplate.Api.http) is this document executable, and the OpenAPI
document is served at `/openapi/v1.json` in development with [Scalar](https://scalar.com) over it
at `/scalar/v1`.

<!--#if (todoLists)-->
- [Authentication](#authentication--apiv1auth)
- [To-do lists](#to-do-lists--apiv1todo-lists)
- [Reminders](#reminders--apiv1reminders-and-apiv1reminders)
- [Files](#files--apiv1files)
- [Notes](#notes--apiv1notes)
- [Account administration](#account-administration--apiv1authaccounts)
- [Maintenance](#maintenance--apiv1maintenance)
<!--#endif-->
<!--#if (todoLists || !notes)-->
<!--#else
- [Authentication](#authentication--apiv1auth)
- [Files](#files--apiv1files)
- [Notes](#notes--apiv1notes)
- [Account administration](#account-administration--apiv1authaccounts)
- [Maintenance](#maintenance--apiv1maintenance)
#endif-->
<!--#if (todoLists || notes)-->
<!--#else
- [Authentication](#authentication--apiv1auth)
- [Files](#files--apiv1files)
- [Account administration](#account-administration--apiv1authaccounts)
- [Maintenance](#maintenance--apiv1maintenance)
#endif-->

## Authentication — `api/v1/auth/*`

Twelve of the twenty-eight actions here are explicitly `[AllowAnonymous]`; the
other sixteen require `[Authorize]` (`logout-all`, `me`, `me/security-events`,
`change-password`, the three `two-factor/*` actions, the four authenticated
`passkeys/*` actions, `change-email`, `confirm-email-change`, `delete-account`,
and the two `sessions` actions). Twenty-one of the twenty-eight are
rate-limited to **10 requests per minute per client IP**; `logout-all`,
`me`, `me/security-events` and the two `sessions` actions deliberately
fall to the global limiter instead, since none is an attempt at a credential —
and so do `GET /auth/passkeys` and `DELETE /auth/passkeys/{credentialId}`,
which present none either.

| Method | Route | Success | Notes |
|---|---|---|---|
| POST | `/api/v1/auth/register` | 200 | Body carries `confirmationEmailSent`; the account is committed before the mail is sent, so a delivery failure is recoverable, not fatal. |
| POST | `/api/v1/auth/login` | 200 | Tagged by a `status` field a client reads rather than guessing from which fields are present: `authenticated` nests the four token fields under `tokens`, `twoFactorRequired` carries a `challengeToken` instead. |
| POST | `/api/v1/auth/login/two-factor` | 200 | Exchanges the challenge token and a code for the same response shape as `login`. |
| POST | `/api/v1/auth/login/external` | 200 | The client runs the provider's OAuth/PKCE flow itself and posts `provider` and the `idToken`; the API verifies it against that provider's JWKS and mints its own pair. Same `status` tag as `login`, plus `accountCreated`. |
| POST | `/api/v1/auth/refresh` | 200 | Consumes the presented refresh token and returns a new pair. |
| POST | `/api/v1/auth/confirm-email` | 204 | **POST with a JSON body**, not a GET with a query string. |
| POST | `/api/v1/auth/resend-confirmation-email` | 204 | Always 204, whether or not the address exists. |
| POST | `/api/v1/auth/logout` | 204 | Ends the session the presented refresh token belongs to. Idempotent. |
| POST | `/api/v1/auth/logout-all` | 204 | Authenticated. Ends every session the caller holds. |
| GET | `/api/v1/auth/sessions` | 200 | Authenticated. The caller's live sessions, most recently used first, the one this request is made from carrying `current: true`. |
| GET, HEAD | `/api/v1/auth/me` | 200 | Authenticated. The caller's own profile; takes no input. |
| GET | `/api/v1/auth/me/security-events` | 200 | Authenticated. The caller's own security trail, newest first, offset-paged (`page`/`pageSize`, max 100): every recorded sign-in attempt, lockout and credential change on this account, each with the IP address and user agent it came from. Events older than [`SecurityEventLog:RetentionInDays`](CONFIGURATION.md#securityeventlog) are gone — a page, not a history. |
| POST | `/api/v1/auth/change-password` | 204 | Authenticated. The current password is presented again as proof the session is not a stolen token. **Signs out every other session**: the access token dies with the security stamp and every other session's refresh token is revoked, so the other clients sign in again; the session the change was made from refreshes into the new posture. |
| POST | `/api/v1/auth/two-factor/setup` | 200 | Authenticated. Provisions a shared key; arms nothing on its own. **It rotates the security stamp, so the access token that called it stops working** — sign in again before calling `two-factor/confirm`. |
| POST | `/api/v1/auth/two-factor/confirm` | 200 | Authenticated, and requires the current password: arming a second factor is the irreversible direction, so a stolen session alone must not do it. Confirms enrollment, arms two-factor sign-in, and returns ten recovery codes shown once. Signs out every other session, as `change-password` does. 409 when two-factor sign-in is already on: the codes on file stay the ones shown. |
| POST | `/api/v1/auth/two-factor/disable` | 204 | Authenticated. Requires the current password. Signs out every other session, as `change-password` does. |
| POST | `/api/v1/auth/passkeys/register/options` | 200 | Authenticated. The first step of registering a passkey: creation options for `navigator.credentials.create()`, as a JSON string, and the opaque `registrationState` to post back with the credential that results. Arms nothing — nothing is stored until the completing call. |
| POST | `/api/v1/auth/passkeys/register` | 200 | Authenticated, and **requires re-authentication for the reason `two-factor/confirm` requires it — except that a passkey is a whole way in, not a second step on one**: the current password when the account has one, and for an account holding nothing its holder could present (a provider-linked one) an access token issued less than five minutes ago, answered `403 auth.account.reAuthenticationRequired` when the calling one is older. Body: the JSON-serialised credential, the state, an optional label for the key. 400 `auth.passkey.registrationInvalid` when the ceremony does not verify; the account's sessions are untouched. |
| GET | `/api/v1/auth/passkeys` | 200 | Authenticated. The passkeys registered on the caller's account, the newest first — `id` (base64url, what the removal route takes), `name`, `createdAt`. |
| DELETE | `/api/v1/auth/passkeys/{credentialId}` | 204 | Authenticated. Removes one of the caller's passkeys by the id the passkey list names. 404 for a passkey that belongs to somebody else, exactly as for an unknown id; removing one already removed succeeds again. |
| POST | `/api/v1/auth/passkeys/sign-in/options` | 200 | Anonymous. Request options for `navigator.credentials.get()`, as a JSON string, and the opaque `authenticationState` to post back with the assertion that results. No account is named and none is disclosed: the authenticator names one by offering a discoverable credential. |
| POST | `/api/v1/auth/passkeys/sign-in` | 200 | Anonymous. Exchanges the assertion for the same response shape as `login` — always the `authenticated` branch, since a user-verified passkey is the whole of a sign-in and the account's armed second factor governs only the ways in that prove less. One sign-in, one session: the response's tokens name the session this sign-in started, as a password's do. One 401 `auth.passkeySignIn.invalid` for every refusal. |
| DELETE | `/api/v1/auth/sessions/{sessionId}` | 204 | Authenticated. Ends one of the caller's sessions by the id the session list names — its own included, which signs that client out. 404 for a session that belongs to somebody else, exactly as for an unknown id; ending an already-ended session succeeds again. |
| POST | `/api/v1/auth/change-email` | 204 | Authenticated. Requires the current password; mails a token to the new address. |
| POST | `/api/v1/auth/confirm-email-change` | 204 | Authenticated. Confirms the pending change from the token mailed to the new address. |
| POST | `/api/v1/auth/delete-account` | 204 | Authenticated. Deletes the caller's own account and every session its tokens name — the last answer this token can obtain. **Re-authentication is demanded on the way out**: the current password when the account has one, a two-factor code when the factor is armed, and — for an account with neither — an access token issued less than five minutes ago, answered `403` `auth.account.reAuthenticationRequired` when the calling one is older. What the other features hold under the id is removed through the account-deleted fact each consumes; see `ARCHITECTURE.md`. |
| POST | `/api/v1/auth/forgot-password` | 204 | Always 204, whether or not the address exists. |
| POST | `/api/v1/auth/reset-password` | 204 | **POST with a JSON body**, not a GET with a query string. |

**The refresh token is in the response body, not a cookie.** It is an opaque
32-byte CSPRNG value, base64url-encoded — not a JWT — and only its SHA-256 hash is
stored. Every presentation rotates it. Presenting one that was already rotated or
revoked is treated as theft: **every session of that user is revoked** and the
request fails. Verified: replaying a consumed token returns 401, and the
token it had been rotated into stops working too.

**One sign-in is one session.** A session is the refresh-token family that sign-in
started; the access token issued beside its first grant carries a `sid` claim naming
it, and every refresh stays in the same session. A token of a session that was ended —
signed out, revoked by its owner, or killed with the rest — is refused without the
theft alarm: it is what a client still holding it sends next.

<!--#if (todoLists)-->
## To-do lists — `api/v1/todo-lists/*`

Authentication required (no opt-out). One controller for one aggregate root; items
are addressed **through their list**, because that is what the aggregate boundary
means — there is no route that reaches an item without naming its list. Every write
below answers `200` with the changed representation and its new `ETag`, except
creating a list or an item (`201`) and deleting a list (`204`).

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/todo-lists?page=1&pageSize=20&sort=createdAt:desc` | 200 — paged summaries of the caller's own lists |
| GET | `/api/v1/todo-lists/{todoListId}` | 200 — the list with its items and tags |
| GET | `/api/v1/todo-lists/{todoListId}/items` | 200 — every item of the list |
| GET | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}` | 200 |
| POST | `/api/v1/todo-lists` | 201 + `Location` |
| PUT | `/api/v1/todo-lists/{todoListId}` | 200 — rename; the id comes from the route, never the body |
| DELETE | `/api/v1/todo-lists/{todoListId}` | 204 |
| POST | `/api/v1/todo-lists/{todoListId}/items` | 201 + `Location` |
| PUT | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}` | 200 — replaces title and description |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/complete` | 200 |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reopen` | 200 |
| DELETE | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}` | 200 — answers with the list: the item this route named no longer exists to have its own representation |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/tags` | 200 |
| PUT | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/tags` | 200 — replaces the whole tag set |
| DELETE | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/tags/{tag}` | 200 |
| GET | `/api/v1/todo-lists/tags` | 200 — the tags the caller has already used on their items, for a picker or a filter. Read through the cache, so a tag added moments ago may be missing |

## Reminders — `api/v1/.../reminders` and `api/v1/reminders/*`

Authentication required (no opt-out) — as with to-do lists, these actions declare neither
`[Authorize]` nor `[AllowAnonymous]` and rely entirely on the default-deny fallback policy. A reminder is its own aggregate root, addressed
independently of the list or item it is about once scheduled — scheduling and listing
go through the item, rescheduling and cancelling go through the reminder's own id.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reminders` | 200 — every reminder of the caller's scheduled for that item. **200 with an empty list, not 404, for an item that does not exist or is somebody else's** — the one route into an item that does not answer 404, because a reminder outlives the item it is about and this is the only route that can show a cancelled one. Nothing leaks: a stranger sees the same empty list either way |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reminders` | 201 + `Location` — points at the collection above; there is no single-reminder `GET` |
| PUT | `/api/v1/reminders/{reminderId}` | 200 — reschedule |
| DELETE | `/api/v1/reminders/{reminderId}` | 204 — cancel |
<!--#endif-->

## Files — `api/v1/files/*`

Authentication required (no opt-out). A file is deposited in one of two ways, and both end in
the same state machine (`pending` → `deposited` → `available` | `quarantined`):

- **Directly on the store** (S3 only): the API signs URLs and the client talks to the object
  store, so depositing is two API calls around a `PUT` to the store, and reading is a redirect.
  No byte passes through the API on this path. With the local file system store
  (`Files:Storage:Provider` = `FileSystem`) nothing can be signed: registering for a direct upload
  answers `409` `storedFile.directUploadUnavailable`, and reading answers `200` with the bytes.
- **Through the API** (both stores): one `multipart/form-data` request to
  `POST /api/v1/files/uploads`, bounded by `Files:ApiUploadMaxBytes` (10 MiB by default). The only
  endpoint whose body carries file bytes.

A category's `UploadModes` (`Direct`, `Api`) says which of the two it accepts.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/files?page=1&pageSize=20&sort=registeredAt:desc` | 200 — paged summaries of the caller's own files |
| GET, HEAD | `/api/v1/files/{fileId}` | 200 — the file's metadata, and the `ETag` the two writes below are conditioned on; 304 to a matching `If-None-Match` |
| GET | `/api/v1/files/{fileId}/content` | **302** + `Location` — a short-lived signed URL, and no body; or, with the file system store, **200** and the bytes as an attachment. The owner's file, or any available file of an `Authenticated` category |
| POST | `/api/v1/files` | 201 + `Location` — reserves a place and returns the upload grant |
| POST | `/api/v1/files/uploads` | 201 + `Location` and `ETag` — `multipart/form-data`; registers the file, stores its bytes and answers it `deposited` |
| POST | `/api/v1/files/{fileId}/confirm` | 200 — the file's metadata, with its new version |
| PUT | `/api/v1/files/{fileId}/tags` | 200 — replaces the whole tag set, conditioned on the `ETag` above, by the same domain rules a to-do item's tags obey |
| GET | `/api/v1/files/tags` | 200 — the tags the caller has already used on their files, for a picker or a filter. Read through the cache, so a tag added moments ago may be missing |
| DELETE | `/api/v1/files/{fileId}` | 204 |

**Depositing directly, in order.**

1. `POST /api/v1/files` with metadata only — `category`, `name`, `declaredMediaType`,
   `sizeInBytes` and `checksum` (SHA-256, 64 hexadecimal characters). The category is one of
   `Files:Categories` and decides the media types, the maximum size, the upload modes, the
   inspection and the quota that apply. The answer is the file's `id` and
   an `upload` grant: a signed `url`, the `method` the signature covers, the
   `requiredHeaders` the deposit must send back verbatim, and an `expiresAt`.
2. Send the bytes straight at that URL, with the grant's `method` (`PUT` in both shipped
   adapters) and its `requiredHeaders` unchanged. The signature covers the media type, the
   length and the checksum, so a deposit that does not match what was declared is refused
   by the store, with nothing written.
3. `POST /api/v1/files/{fileId}/confirm`, which asks the store what it actually holds and
   moves the file from `pending` to `deposited` only if that agrees with the declaration.

Splitting it across two API calls keeps large files off the API:
`RequestLimits:MaxRequestBodyBytes` caps an inbound body at 64 KiB, and the idempotency
filter buffers and SHA-256s the whole body of every `[Idempotent]` `POST`. The registration
body is metadata — a few hundred characters whatever the file weighs.

**Uploading through the API.** `POST /api/v1/files/uploads` with a `multipart/form-data` body:

| Field | Required | Meaning |
|---|---|---|
| `category` | yes | One of `Files:Categories`, with `Api` among its `UploadModes` |
| `file` | yes, exactly one file part | The content. The part's `Content-Type` is the declared media type and must be allowed by the category |
| `name` | no | The label; the part's file name when absent or blank. Held to the same rules as a registered name |

The API computes the SHA-256 itself, registers the file (`pending`, committed, quota applied
as for a direct registration), writes the bytes to the store outside the quota lock, then
confirms the deposit and answers `201` with the file, `status` `deposited`, its `ETag` and a
`Location`. The body may be `Files:ApiUploadMaxBytes` plus 16 KiB of multipart framing; a
larger body is `413` `request.tooLarge`, refused before it is read when it declares its length.
A file above `Files:ApiUploadMaxBytes` or above the category's maximum, a missing or second file
part, or a media type the category does not allow is `400` on `file`. A category without `Api`
is `409` `storedFile.apiUploadUnavailable`; the quota answers as below. A body that is not
`multipart/form-data` is `415`.

Not `[Idempotent]` — the filter would buffer the whole file — so a retried request uploads a
second file; a client unsure whether its first attempt arrived lists its files. If the write
or the confirmation fails after the registration, the registration is removed at once, freeing
its quota slot; should that removal fail too, the abandonment purge and the orphan sweep reclaim
the row and the bytes. The response is `no-store`.

**Confirming does not make the file readable.** It leaves the file `deposited` — the bytes
arrived and are the ones declared, but nothing has looked at them — until the inspection
job has, and `FileWorker:InspectDepositedFilesInterval`, one minute by default,
is a latency a user feels. `availableAt` is `null` until then.

`status` has **four** values, and a client that branches on it must handle all of them:

| `status` | What it means |
|---|---|
| `pending` | Registered, an object key reserved, nothing deposited against it yet. The abandonment sweep eventually removes one that stays here. |
| `deposited` | **What `confirm` answers.** The bytes are present and match the declaration; no verdict has been reached on what they are. Not servable. |
| `available` | Inspected and cleared. The only state whose content can be fetched. |
| `quarantined` | Inspected and refused. Terminal — it never becomes available, however long the caller waits. |

Asking for content before a verdict is `409` `storedFile.notAvailable`; asking for content
that was examined and refused is `409` `storedFile.quarantined`.

**Reading is a redirect.** `GET /api/v1/files/{fileId}/content` answers `302` with a signed
URL, so an `<img>`, a download manager or `curl -L` follows it with no client code. That
`Location` is a bearer credential — whoever holds it reads the file, with no identity
attached — so the response is `Cache-Control: no-store` and the URL must not be logged,
stored or shared. With the file system store the same request answers `200` with the bytes,
`Content-Disposition: attachment`, the declared media type and `X-Content-Type-Options: nosniff`. `POST /api/v1/files` carries a signed *write* URL and is `no-store` for
the same reason.

**Registration is the endpoint `Idempotency-Key` exists for.** It is unaddressed creation:
a retry that is not recognised mints a second file, a second object key and a second grant.
`confirm` deliberately takes no key — it names the file it acts on, and the transition is
one-way, so a second call meets a file that is no longer pending and gets `409`.

**Conditional requests.** `GET`/`HEAD` on one file publishes its version as a strong `ETag`;
`confirm` and `DELETE` honour `If-Match` and answer `412` when it is stale, `428` when
`Concurrency:IfMatch` is `Required` and none was sent. `If-Match: *` is the useful form
here: a registration nothing was ever deposited against is removed by the abandonment
sweep, so a client resuming after a long upload gets `412` rather than a `404` it might
read as "wrong id". Registration takes no precondition — there is no resource yet to have
a version.

**What refuses a direct registration.** `400` on `category` for a category the deployment does not
declare, on `mediaType` for a type the category does not allow, and on `sizeInBytes` above the
category's maximum (a single file may never exceed 5 GiB). `409`
`storedFile.directUploadUnavailable` when the store cannot sign (the file system store) or the
category allows only uploads through the API. `409` `storedFile.quotaExceeded` covers the three
bounds one owner has **in that category** — by default 20 uploads outstanding at once, 1 000
files and 10 GiB of committed bytes, each set per category in configuration. `409` also carries
a value the domain refuses that this layer cannot restate — a reserved device name, a wildcard
media type, a checksum of the right length that is not hexadecimal.

**Every file carries its `category`**, in every representation. A business feature that needs
a file stores its `id` and names the category it registers under.

Sorting is `name`, `registeredAt` and `availableAt`; `availableAt` is offset-only because
its column is nullable — asking for it with `paging=cursor` is a `400` `cursor.invalid`.
`search` matches the file name, `state` narrows to any one of the four values above.
Everything else in
[Collection queries](API-CONVENTIONS.md#collection-queries--sorting-filtering-paging) applies unchanged.

A file that belongs to somebody else answers exactly as an absent one does — `404`
`storedFile.notFound` — because a `403` next to a `404` is how an id becomes a probe. One
exception, and only for the content: a category whose `Visibility` is `Authenticated` lets any
signed-in user download an **available** file of it by its id. Its metadata, its tags, its
confirmation and its deletion stay the owner's, and a non-owner asking for the content of such a
file that is not available yet gets the same `404`. A file whose category was removed from
configuration is treated as `Private`.

<!--#if (notes)-->
## Notes — `api/v1/notes/*`

Authentication required (no opt-out). A note is the feature with no domain model: five routes over
one service. The owner is always the caller and is not a field of any body, and a note that belongs
to somebody else answers exactly as an absent one — `404` `note.notFound` — on every route, with or
without an `If-Match`.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/notes?page=1&pageSize=20&sort=createdAt:desc` | 200 — paged summaries (id, title, instants, no body) of the caller's own notes |
| GET, HEAD | `/api/v1/notes/{noteId}` | 200 — the note, and the `ETag` the two writes below are conditioned on; 304 to a matching `If-None-Match` |
| POST | `/api/v1/notes` | 201 + `Location` and `ETag` |
| PUT | `/api/v1/notes/{noteId}` | 200 — replaces title and body, answers with the new `ETag`; the id comes from the route, never the body |
| DELETE | `/api/v1/notes/{noteId}` | 204 |

A title is required and at most 200 characters once trimmed, and is stored trimmed; a body is
required, may be empty, and is at most 10 000 characters. Both limits are `400`
`request.validationFailed`. Sorting is `title` and `createdAt`, both keyset-capable, and there is
no filter. Everything else in
[Collection queries](API-CONVENTIONS.md#collection-queries--sorting-filtering-paging) applies
unchanged.

**What each write guarantees, and what a retry does.**

| Write | Transaction | Concurrency | Retry |
|---|---|---|---|
| `POST` | One save of one row | None: there is no note yet to have a version | Safe with an `Idempotency-Key`, which replays the first response; without one, a repeat creates a second note |
| `PUT` | One `UPDATE` conditioned on the `xmin` it loaded | `If-Match` stale: `412` `precondition.failed`. A writer that loses the race at the write: `409` `concurrency.conflict`. No `If-Match`: accepted, last write wins, unless `Concurrency:IfMatch` is `Required` (`428`) | The same `If-Match` sent again meets a version that has moved and is refused, so the change applies once |
| `DELETE` | One `DELETE` conditioned on the `xmin` it loaded | As `PUT` | The note is gone, so a repeat is `404` (`412` with `If-Match: *`) |
<!--#endif-->

## Account administration — `api/v1/auth/accounts/*`

`{role}` is a **role name**, and one role ships: `Admin`. `Administrator` is the name of the
*policy* these endpoints require, not of the role that satisfies it, and sending it answers
`400` `Role 'Administrator' does not exist.` The names live in `IdentityRoles`.

Requires the `Administrator` policy on the whole controller — an authenticated
non-admin gets `403`. Acting on somebody else's account: every write below refuses
with `403` when the target id names the caller. The trail read is the exception:
reading one's own events is exactly what `GET /auth/me/security-events` offers.

| Method | Route | Success |
|---|---|---|
| POST | `/api/v1/auth/accounts/{userId}/lockout` | 204 — locks the account out indefinitely and rotates its security stamp |
| DELETE | `/api/v1/auth/accounts/{userId}/lockout` | 204 — lifts the lockout; a no-op on an account that was not locked |
| PUT | `/api/v1/auth/accounts/{userId}/roles/{role}` | 204 — grants a role; 400 if the role does not exist, or if the account already has it |
| DELETE | `/api/v1/auth/accounts/{userId}/roles/{role}` | 204 — revokes a role; 400 if the role does not exist, or if the account does not have it |
| DELETE | `/api/v1/auth/accounts/{userId}/two-factor` | 204 — disarms the account's second factor and rotates its security stamp; the way back for a lost phone and lost recovery codes |
| DELETE | `/api/v1/auth/accounts/{userId}` | 204 — deletes the account outright; what the other features hold under the id is removed through the account-deleted fact each consumes, not by this call |
| GET | `/api/v1/auth/accounts/{userId}/security-events` | 200 — that account's security trail, newest first, offset-paged; **no 404 for an unknown id**: the trail is kept after an account is deleted until retention expires, so the rows an investigation needs are the ones that survive the account, and an id naming nothing answers an empty page |

<!--#if (externalAuth)
## External identities — `api/v1/external-identities/*`

Requires the `Administrator` policy on the whole controller — an authenticated
non-admin gets `403`. This API holds no passwords and mints no tokens: the provider
does both, so the one administrative act left here is deleting an account's local
presence.

| Method | Route | Success |
|---|---|---|
| DELETE | `/api/v1/external-identities/{userId}` | 204 — deletes every mapping that names the owner and its assigned roles; 404 when no mapping names the id |

The data the features hold under the id is removed through the account-deleted fact
each consumes; see `ARCHITECTURE.md`. A caller who signs in again is provisioned under
a new owner id, with none of the deleted one's data — that is what deleting the
account meant, and the provider's own account is the provider's to delete. When the
provider retires a subject, an administrator calls this with the owner id, or the
project wires the provider's own deletion signal to the same service; the template
ships no endpoint for that signal. This trigger records no security event: the
persisted trail is the Auth module's, and this template has no Auth module — audit
deletions here through the provider's own logs.

#endif-->
## Maintenance — `api/v1/maintenance/*`

| Method | Route | Success |
|---|---|---|
| DELETE | `/api/v1/maintenance/idempotency-keys/expired` | 200 — the number of rows removed |
| DELETE | `/api/v1/maintenance/refresh-tokens/expired` | 200 — the number of rows removed |
| DELETE | `/api/v1/maintenance/security-events/expired` | 200 — the number of rows removed; "expired" is [`SecurityEventLog:RetentionInDays`](CONFIGURATION.md#securityeventlog), not a parameter |

Requires the `Administrator` policy — an authenticated non-admin gets `403`. Together
with the seven `api/v1/auth/accounts/*` actions above, these are the ten endpoints
whose authority is more than "authenticated plus ownership"; the three here exist
because the idempotency store, the refresh-token table and the security-event trail
each grow until something prunes them. Schedule all three.

