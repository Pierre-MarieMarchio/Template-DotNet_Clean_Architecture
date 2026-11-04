# Notes

How this project keeps a caller's notes: a title and a body, owned by whoever wrote them. Notes is
the simple example, a service over a store with no domain model.

## What you get

A signed-in caller creates notes, lists their own, reads one, replaces its title and body, and deletes
it. A note belongs to its creator: another caller's note answers `404` `note.notFound` exactly as a
missing one does ([ADR 0009](../decisions/0009-ownership-is-in-the-query.md)). Once an account is
deleted, its notes are deleted too ([ADR 0012](../decisions/0012-account-deletion-each-feature-decides.md)).

A title is required and at most 200 characters once trimmed. A body is required but may be empty, and
holds at most 10000 characters.

## How it works

Notes has no rule to protect, so it has no aggregate: this is the simple shape of
[feature shapes](../architecture/feature-shapes.md). `INoteService` carries each operation and
validates its input. `INoteStore` stages a write and `INoteQueries` reads, both scoped to the caller,
and a save commits what was staged. The rows live in the `notes` schema, in the `Notes` table, with
one version column that every write changes.

Every note answers with its version, which the presentation layer turns into the `ETag`. A write may
carry the version it read: a stale one is refused, and one for a note that is missing or another
caller's is `404`. The list is sorted by `title` or `createdAt`, newest first by default, and pages
of 20 by default and 100 at most, by offset or by cursor.

A host that holds the application layer composes Notes with `AddNotes()` in its own `Program.cs` and reaches
it through `INoteService`. An MCP server, when this project holds one, offers it as the tools
`list_notes`, `get_note` and `create_note`, in its `Features/Notes/Tools` folder: the first two need the
`read` scope of the caller's credential, the last one `write`.

## Use it

<!--#if (api)-->
The API serves Notes under `/api/v1/notes`, for a signed-in caller:

| Request | Answers |
|---|---|
| `GET /api/v1/notes` | `200`, a page of summaries without the body; `400` on a bad `sort` or page |
| `GET /api/v1/notes/{noteId}`, and `HEAD` | `200` with the note and its `ETag`; `304` to a matching `If-None-Match`; `404` |
| `POST /api/v1/notes` | `201` with `Location` and the `ETag`; `400`; `409` while the same `Idempotency-Key` is in flight |
| `PUT /api/v1/notes/{noteId}` | `200` with the new `ETag`; `400`; `404`; `412` stale `If-Match`; `428` when `If-Match` is required |
| `DELETE /api/v1/notes/{noteId}` | `204`; `404`; `412`; `428` |

A note is created like this, and replaced with the `ETag` the creation returned:

```bash
API=http://localhost:8080    # http://localhost:5187 on the host

curl -X POST "$API"/api/v1/notes \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -d '{"title": "Groceries", "body": "Milk, eggs"}'

curl -X PUT "$API"/api/v1/notes/$NOTE_ID \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -H 'If-Match: "AAAABw"' \
  -d '{"title": "Groceries", "body": "Milk, eggs, flour"}'
```

A client handles `404` as a missing note whoever owns it, and `412` by reading the note again before
retrying. [The HTTP API](../architecture/http-api.md#etag-and-if-match) and the
[API reference](../reference/api.md) hold the rules every endpoint shares and the error codes.

<!--#endif-->
<!--#if (!api)-->
No HTTP route serves Notes in this project: a host that holds the application layer calls `INoteService` for the caller it names.

<!--#endif-->
## Configure

Nothing to configure for Notes itself: its limits are constants of `NoteInputPolicy` and
`NoteCollectionPolicy`.
<!--#if (api)-->
The `ETag` precondition and the idempotency of a create read
[`Concurrency`](../reference/configuration.md#concurrency) and
[`Idempotency`](../reference/configuration.md#idempotency).
<!--#endif-->

## Tests

The service, its input rules, the sort and page policy and the account-deletion consumer are tested in
`Tests/Application/AT.Application.UnitTests/Features/Notes`, and the row mapping and the sort map in
`Tests/Infrastructure/AT.Infrastructure.Persistence.UnitTests/Features/Notes`.
<!--#if (api)-->
The integration tests in `Tests/Integration/AT.Api.IntegrationTests/Notes` run the routes against a
database: creation and reading, another caller's note, the version precondition and the idempotent
create. `Tests/Presentation/AT.Api.UnitTests/Composition/NotesSavingTests.cs` shows a write saved
through a real context.
<!--#endif-->
An MCP server's own tests for these tools are in its test project, under `Features/Notes/Tools`.

## Decisions

- [ADR 0002: two feature shapes](../decisions/0002-two-feature-shapes.md)
- [ADR 0008: optimistic concurrency end to end](../decisions/0008-optimistic-concurrency-end-to-end.md)
- [ADR 0009: ownership is in the query](../decisions/0009-ownership-is-in-the-query.md)
- [ADR 0012: on account deletion, each feature decides](../decisions/0012-account-deletion-each-feature-decides.md)
