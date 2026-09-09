# API reference

The endpoints, one section per feature. What holds for all of them — versioning, default-deny
authorisation, the `code` a failure carries, `ETag`, `Idempotency-Key`, the collection contract and
the rate limits — is in [`API-CONVENTIONS.md`](API-CONVENTIONS.md). Read that first; nothing here
repeats it.

[`AppTemplate.Api.http`](../AppTemplate.Api.http) is this document executable, and the OpenAPI
document is served at `/openapi/v1.json` in development with [Scalar](https://scalar.com) over it
at `/scalar/v1`.

- [Authentication](#authentication--apiv1auth)
- [To-do lists](#to-do-lists--apiv1todo-lists)
- [Reminders](#reminders--apiv1reminders-and-apiv1reminders)
- [Files](#files--apiv1files)
- [Account administration](#account-administration--apiv1authaccounts)
- [Maintenance](#maintenance--apiv1maintenance)

## Authentication — `api/v1/auth/*`

Ten of the eighteen actions here are explicitly `[AllowAnonymous]`; the
other eight require `[Authorize]` (`logout-all`, `me`, `change-password`, the three
`two-factor/*` actions, `change-email`, `confirm-email-change`). Sixteen of the
eighteen are rate-limited to **10 requests per minute per client IP**; `logout-all`
and `me` deliberately fall to the global limiter instead, since neither is an attempt
at a credential.

| Method | Route | Success | Notes |
|---|---|---|---|
| POST | `/api/v1/auth/register` | 200 | Body carries `confirmationEmailSent`; the account is committed before the mail is sent, so a delivery failure is recoverable, not fatal. |
| POST | `/api/v1/auth/login` | 200 | Tagged by a `status` field a client reads rather than guessing from which fields are present: `authenticated` nests the four token fields under `tokens`, `twoFactorRequired` carries a `challengeToken` instead. |
| POST | `/api/v1/auth/login/two-factor` | 200 | Exchanges the challenge token and a code for the same response shape as `login`. |
| POST | `/api/v1/auth/login/external` | 200 | The client runs the provider's OAuth/PKCE flow itself and posts `provider` and the `idToken`; the API verifies it against that provider's JWKS and mints its own pair. Same `status` tag as `login`, plus `accountCreated`. |
| POST | `/api/v1/auth/refresh` | 200 | Consumes the presented refresh token and returns a new pair. |
| POST | `/api/v1/auth/confirm-email` | 204 | **POST with a JSON body**, not a GET with a query string. |
| POST | `/api/v1/auth/resend-confirmation-email` | 204 | Always 204, whether or not the address exists. |
| POST | `/api/v1/auth/logout` | 204 | Revokes the presented refresh token. Idempotent. |
| POST | `/api/v1/auth/logout-all` | 204 | Authenticated. Revokes every refresh token grant the caller holds. |
| GET | `/api/v1/auth/me` | 200 | Authenticated. The caller's own profile; takes no input. |
| POST | `/api/v1/auth/change-password` | 204 | Authenticated. The current password is presented again as proof the session is not a stolen token. |
| POST | `/api/v1/auth/two-factor/setup` | 200 | Authenticated. Provisions a shared key; arms nothing on its own. **It rotates the security stamp, so the access token that called it stops working** — sign in again before calling `two-factor/confirm`. |
| POST | `/api/v1/auth/two-factor/confirm` | 200 | Authenticated, and requires the current password: arming a second factor is the irreversible direction, so a stolen session alone must not do it. Confirms enrollment, arms two-factor sign-in, and returns ten recovery codes shown once. |
| POST | `/api/v1/auth/two-factor/disable` | 204 | Authenticated. Requires the current password. |
| POST | `/api/v1/auth/change-email` | 204 | Authenticated. Requires the current password; mails a token to the new address. |
| POST | `/api/v1/auth/confirm-email-change` | 204 | Authenticated. Confirms the pending change from the token mailed to the new address. |
| POST | `/api/v1/auth/forgot-password` | 204 | Always 204, whether or not the address exists. |
| POST | `/api/v1/auth/reset-password` | 204 | **POST with a JSON body**, not a GET with a query string. |

**The refresh token is in the response body, not a cookie.** It is an opaque
32-byte CSPRNG value, base64url-encoded — not a JWT — and only its SHA-256 hash is
stored. Every presentation rotates it. Presenting one that was already rotated or
revoked is treated as theft: the **entire token family for that user is revoked** and
the request fails. Verified: replaying a consumed token returns 401, and the
token it had been rotated into stops working too.

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

## Files — `api/v1/files/*`

Authentication required (no opt-out). **No byte of any file passes through this API, in
either direction** — that is the one thing to understand before calling anything here.
The API signs URLs and the client talks to the object store directly, so depositing a
file is two requests and reading one back is a redirect.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/files?page=1&pageSize=20&sort=registeredAt:desc` | 200 — paged summaries of the caller's own files |
| GET, HEAD | `/api/v1/files/{fileId}` | 200 — the file's metadata, and the `ETag` the two writes below are conditioned on; 304 to a matching `If-None-Match` |
| GET | `/api/v1/files/{fileId}/content` | **302** + `Location` — a short-lived signed URL, and no body |
| POST | `/api/v1/files` | 201 + `Location` — reserves a place and returns the upload grant |
| POST | `/api/v1/files/{fileId}/confirm` | 200 — the file's metadata, with its new version |
| PUT | `/api/v1/files/{fileId}/tags` | 200 — replaces the whole tag set, conditioned on the `ETag` above, by the same domain rules a to-do item's tags obey |
| GET | `/api/v1/files/tags` | 200 — the tags the caller has already used on their files, for a picker or a filter. Read through the cache, so a tag added moments ago may be missing |
| DELETE | `/api/v1/files/{fileId}` | 204 |

**Depositing, in order.**

1. `POST /api/v1/files` with metadata only — `name`, `declaredMediaType`, `sizeInBytes`
   and `checksum` (SHA-256, 64 hexadecimal characters). The answer is the file's `id` and
   an `upload` grant: a signed `url`, the `method` the signature covers, the
   `requiredHeaders` the deposit must send back verbatim, and an `expiresAt`.
2. Send the bytes straight at that URL, with the grant's `method` (`PUT` in both shipped
   adapters) and its `requiredHeaders` unchanged. The signature covers the media type, the
   length and the checksum, so a deposit that does not match what was declared is refused
   by the store, with nothing written.
3. `POST /api/v1/files/{fileId}/confirm`, which asks the store what it actually holds and
   moves the file from `pending` to `deposited` only if that agrees with the declaration.

Splitting it across two API calls is forced rather than chosen:
`RequestLimits:MaxRequestBodyBytes` caps an inbound body at 64 KiB, and the idempotency
filter buffers and SHA-256s the whole body of every `POST` before a handler sees it. Every
body on this controller is metadata — a name, a media type, a length, a digest — a few
hundred characters whatever the file weighs, which is why no action here raises the limit.

**Confirming does not make the file readable.** It leaves the file `deposited` — the bytes
arrived and are the ones declared, but nothing has looked at them — until the Worker's
inspection pass has, and `FileWorker:InspectDepositedFilesInterval`, one minute by default,
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
stored or shared. `POST /api/v1/files` carries a signed *write* URL and is `no-store` for
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

**What refuses a registration.** `409` `storedFile.quotaExceeded` covers the three bounds
one owner has: 20 uploads outstanding at once, 1 000 files, and 10 GiB of committed bytes
(a single file may not exceed 5 GiB). `409` also carries a value the domain refuses that
this layer cannot restate — a reserved device name, a wildcard media type, a checksum of
the right length that is not hexadecimal.

Sorting is `name`, `registeredAt` and `availableAt`; `availableAt` is offset-only because
its column is nullable — asking for it with `paging=cursor` is a `400` `cursor.invalid`.
`search` matches the file name, `state` narrows to any one of the four values above.
Everything else in
[Collection queries](API-CONVENTIONS.md#collection-queries--sorting-filtering-paging) applies unchanged.

A file that belongs to somebody else answers exactly as an absent one does — `404`
`storedFile.notFound` — because a `403` next to a `404` is how an id becomes a probe.

## Account administration — `api/v1/auth/accounts/*`

`{role}` is a **role name**, and one role ships: `Admin`. `Administrator` is the name of the
*policy* these endpoints require, not of the role that satisfies it, and sending it answers
`400` `Role 'Administrator' does not exist.` The names live in `IdentityRoles`.

Requires the `Administrator` policy on the whole controller — an authenticated
non-admin gets `403`. Acting on somebody else's account: every action below refuses
with `403` when the target id names the caller.

| Method | Route | Success |
|---|---|---|
| POST | `/api/v1/auth/accounts/{userId}/lockout` | 204 — locks the account out indefinitely and rotates its security stamp |
| DELETE | `/api/v1/auth/accounts/{userId}/lockout` | 204 — lifts the lockout; a no-op on an account that was not locked |
| PUT | `/api/v1/auth/accounts/{userId}/roles/{role}` | 204 — grants a role; 400 if the role does not exist, or if the account already has it |
| DELETE | `/api/v1/auth/accounts/{userId}/roles/{role}` | 204 — revokes a role; 400 if the role does not exist, or if the account does not have it |
| DELETE | `/api/v1/auth/accounts/{userId}/two-factor` | 204 — disarms the account's second factor and rotates its security stamp; the way back for a lost phone and lost recovery codes |
| DELETE | `/api/v1/auth/accounts/{userId}` | 204 — deletes the account outright |

## Maintenance — `api/v1/maintenance/*`

| Method | Route | Success |
|---|---|---|
| DELETE | `/api/v1/maintenance/idempotency-keys/expired` | 200 — the number of rows removed |
| DELETE | `/api/v1/maintenance/refresh-tokens/expired` | 200 — the number of rows removed |

Requires the `Administrator` policy — an authenticated non-admin gets `403`. Together
with the six `api/v1/auth/accounts/*` actions above, these are the eight endpoints
whose authority is more than "authenticated plus ownership"; the two here exist
because the idempotency store and the refresh-token table each grow until something
prunes them. Schedule both.

