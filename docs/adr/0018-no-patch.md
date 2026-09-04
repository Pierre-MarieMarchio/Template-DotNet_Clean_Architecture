# 0018 — No `PATCH`: writes are named operations

Status: Accepted

## Context

`TodoListsController` exposes `POST` to create, `PUT` on `{todoListId}` to rename,
`DELETE` to remove, and a set of intent-named operations underneath the list:
`POST .../items` to add one, `POST .../items/{todoItemId}/complete` to complete one, and
`DELETE .../items/{todoItemId}` to remove one. There is no `PATCH` anywhere on this
surface, and none is planned for the sorting, filtering and idempotency-key work landing
alongside this record.

Two standard shapes were the alternative: JSON Merge Patch (RFC 7396), which merges a
partial JSON document onto the resource, and JSON Patch (RFC 6902), which applies a
sequence of add/remove/replace operations against a document.

## Decision

**Every write is a named operation on the aggregate, never a patch against its
representation.** A client that wants to change a `TodoList`'s name calls `Rename`; a
client that wants to mark an item done calls `CompleteItem`. There is no endpoint that
accepts an arbitrary subset of fields and assigns them.

## Consequences

- A client that wants to change one field of a large resource sends the whole
  representation to the operation that owns that field — there is no cheaper path. If a
  resource grows large enough that this becomes the dominant cost of the write, that is a
  cost this decision accepts today.
- A client that wants two changes to land atomically needs an operation that names the
  pair; there is no generic multi-field patch to fall back on. Today's surface does not
  have such a pair, so nothing is missing yet — but the next feature that needs one has to
  add the operation, not reach for `PATCH`.
- Every route stays a verb a reviewer can reason about against the aggregate's own
  invariants, because it is the aggregate's method that runs, not a field-by-field
  assignment chosen by the caller.

## Alternatives rejected

- **JSON Patch (RFC 6902).** An operation script (`replace`, `remove`, `test`, ...)
  addressed against a JSON document by path. This API does not store or serve its
  resources as a document a path can address into — it stores an aggregate — so applying
  a patch means first inventing a document projection solely to receive one, and then
  translating each operation back into an aggregate mutation the domain has to accept.
  That translation is exactly the place an operation can smuggle in a state change no
  method on `TodoList` authorises, which is the anemic-model shape
  [0003](0003-aggregate-oriented-repository.md) already rejected for the write side.
- **JSON Merge Patch (RFC 7396).** Simpler syntax, same problem, plus its own: `null`
  means "remove this field" and an absent field means "leave it alone", a distinction that
  has to be hand-decoded per field and gets easy to get backwards for a field where `null`
  is itself a meaningful value.
- **A hand-rolled partial-update DTO with nullable fields** (e.g. `string? Name`, `bool?
  IsCompleted`, patch whichever are non-null). This is merge-patch with the RFC's name
  removed and its ambiguity kept: "was this field intentionally cleared or just not sent"
  is still unanswerable for any field whose real value can itself be `null`.

## Revisit when

A resource on this surface grows large enough that resending it dominates the cost of a
one-field change, or a client has a legitimate need to change one field without reading
the rest first. Either is the point to add a specific, named operation for that field —
not a general patch endpoint.
