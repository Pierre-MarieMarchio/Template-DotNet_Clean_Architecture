# Files

How this project stores the files its users upload, checks them before anyone reads them, and
serves them back.

## What you get

A signed-in caller registers a file under a category, deposits its bytes, and reads it back once
the file has been inspected. The bytes live in an S3-compatible bucket or a directory; the rows
that describe them live in the `files` schema. Every file has an owner, and another caller's file
answers `404` exactly as a missing one does. A feature that needs a file stores its `id`.

A file moves through four states, and a client must handle all four:

| `status` | Meaning |
|---|---|
| `pending` | registered, an object key reserved, no bytes yet |
| `deposited` | the bytes arrived and match the declaration; not inspected, not readable |
| `available` | inspected and cleared: the only state whose content can be read |
| `quarantined` | inspected and refused; terminal |

## Categories and visibility

A category, declared under `Files:Categories`, decides everything about a file: the media types
allowed, the largest size, the upload modes, whether the content is scanned and whether its
leading bytes must match the declared type, who may read it, and the quota. A file keeps the
category it was registered under. This project declares two:

| Category | Types | Largest | Visibility |
|---|---|---|---|
| `document` | PDF, CSV, plain text | 50 MiB | `Private` |
| `image` | PNG, JPEG, WebP | 10 MiB | `Authenticated` |

`Private` lets only the owner read the content. `Authenticated` lets any signed-in caller read an
available file's content by its id, the shape of an avatar: the id then works as a capability, so
keep confidential files out of such a category. Metadata, tags and deletion stay the owner's
either way. A file whose category was removed from configuration stays readable by its owner and
is treated as `Private`.

## Uploading: directly or through the API

A category's `UploadModes` allows one way or both.

**Directly on the store** (`Direct`, S3 only), so no byte passes through the API:

1. `POST /api/v1/files` with the metadata: `category`, `name`, `declaredMediaType`,
   `sizeInBytes` and `checksum` (SHA-256 in hexadecimal). The answer carries the file's `id` and an
   upload grant: a signed `url`, its `method`, the `requiredHeaders` to send back unchanged, and
   `expiresAt`. Send an `Idempotency-Key`: a retry without one creates a second file.
2. Send the bytes to the grant's URL. The signature covers the type, the length and the digest, so
   the store refuses any other content.
3. `POST /api/v1/files/{fileId}/confirm`. The API asks the store what it holds and moves the file
   to `deposited` only if that matches the declaration.

**Through the API** (`Api`): one `multipart/form-data` request to `POST /api/v1/files/uploads`
with `category`, one `file` part and an optional `name`, up to `Files:ApiUploadMaxBytes` (10 MiB
by default). The API computes the digest, registers, stores and confirms, and answers `201` with
the file `deposited`. This route is not idempotent: a retried upload creates a second file.

A name is a label, never a path: separators, control characters and reserved device names are
refused, and the object key is generated apart from it.

## Downloading

`GET /api/v1/files/{fileId}/content` answers `302` to a short-lived signed URL, so an `<img>` or
`curl -L` follows it with no client code. That URL is a bearer right: the response is
`Cache-Control: no-store`, and the URL must not be logged or shared. With the `FileSystem` store
the same request answers `200` with the bytes as an attachment. Content without a verdict yet is
`409` `storedFile.notAvailable`; refused content is `409` `storedFile.quarantined`.

## Storage

| `Files:Storage:Provider` | Direct uploads | Download | For |
|---|---|---|---|
| `S3` (default) | signed grants | `302` to a signed URL | AWS S3, SeaweedFS, MinIO, R2 |
| `FileSystem` | refused (`409` `storedFile.directUploadUnavailable`) | `200`, streamed by the API | one host, a mounted disk |

Locally, Compose runs SeaweedFS as `objectstore`, reached by the API at `objectstore:9000` and by a
browser at `localhost:9000`; `Files:Storage:S3:PublicEndpoint` is the name signed into every URL.
The bucket, or the root directory, belongs to this feature alone: the orphan sweep deletes
whatever no row references. Leave the S3 key pair empty in production so the SDK resolves an
instance role, and never allow anonymous reads on the bucket.

## Inspection and scanning

A deposited file becomes readable only after the inspection pass. It refuses, by quarantine:
markup in any category, whatever was declared, since nothing here sanitises it; leading bytes that
disagree with the declared type, when the category verifies signatures; and content a `clamd`
daemon flags, when the category scans. Scanning is off by default (`Files:Scanning:Enabled`), and
a category may scan only once it is on. A scanner that cannot be reached leaves the file
`deposited` and retries later: no verdict is never a pass. The refusal does not say which check
tripped; the log does.

The inspection interval (`Jobs:Files:InspectDepositedFilesInterval`, one minute) is a latency
users feel: `availableAt` stays `null` until then.

## Quotas

Each category bounds, per owner, the files kept (1 000), the bytes held (10 GiB) and the uploads
waiting for their bytes (20); going over any is `409` `storedFile.quotaExceeded`. In `Strict` mode,
the default, the check and the insert run under a lock on (owner, category); a registration that waits
more than 10 seconds for it is `409` `storedFile.quotaBusy`.
<!--#if (postgreSql)-->
The lock is a PostgreSQL advisory lock, so the bound holds across every instance.
<!--#endif-->
<!--#if (sqlServer)-->
The lock is a SQL Server application lock, so the bound holds across every instance.
<!--#endif-->
<!--#if (sqlite)-->
The lock is held in the process, so the bound holds inside the one instance this project runs.
<!--#endif-->
 `Indicative` takes no lock, and N concurrent registrations may
overshoot by N − 1 files.

## Background passes

Three passes run in the jobs process ([Background jobs](background-jobs.md)), each under the leader
lease, each with its own interval and switch under `Jobs:Files`:

| Pass | Does | On failure |
|---|---|---|
| Inspection | moves `deposited` files to `available` or `quarantined` | commits each file alone; a file without a verdict waits one minute, doubling up to one hour, behind those that waited longer |
| Abandoned registrations | removes `pending` rows whose bytes never came | idempotent: the next pass selects the same rows |
| Orphaned content | deletes objects no row references, walking up to 250 000 keys per pass from a saved checkpoint | every object present when a walk starts is visited within a bounded number of passes, across restarts |

Deleting a file removes its row at once and its bytes on the event that follows; the orphan sweep
is the guarantee behind that event, so never switch it off. Switching inspection off stops the
feature: nothing becomes readable.

## Configure

| Section | What it sets |
|---|---|
| [`Files`](../reference/configuration.md#files) | the categories and the API upload ceiling |
| [`Files:Storage`](../reference/configuration.md#filesstorage) | the provider |
| [`Files:Storage:S3`](../reference/configuration.md#filesstorages3) | bucket, endpoints, credentials, grant lifetime |
| [`Files:Storage:FileSystem`](../reference/configuration.md#filesstoragefilesystem) | the root directory |
| [`Files:Scanning`](../reference/configuration.md#filesscanning) | the `clamd` daemon |
| [`Jobs:Files`](../reference/configuration.md#jobsfiles) | the three passes |

## Tests

<!--#if (api)-->
The domain and use-case rules are unit-tested under `Features/Files` in each test project. The
integration suite runs against the project's database and SeaweedFS: the API flows, ownership, quotas under
concurrency and the passes in `Tests/Integration/AppTemplate.Api.IntegrationTests/Files/`, and the
store itself (signed grants, the public endpoint, the inventory) in
`Tests/Integration/AppTemplate.Api.IntegrationTests/Storage/`.
<!--#endif-->
<!--#if (!api)-->
The domain and use-case rules are unit-tested under `Features/Files` in each test project.
<!--#endif-->

## Decisions

- [0018 Stored files are keyed, granted and refused](../decisions/0018-stored-files-are-keyed-granted-and-refused.md)
- [0006 Events after commit, without an outbox](../decisions/0006-events-after-commit-without-an-outbox.md): why every pass states its guarantee.
- [0001 Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): the S3-compatible store, SeaweedFS locally.
