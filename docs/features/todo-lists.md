# To-do lists

How this project keeps a caller's to-do lists and the items on them. TodoLists is the rich example:
an aggregate that protects its own rules, and the feature [Reminders](reminders.md) leans on.

## What you get

A signed-in caller creates lists, renames and deletes them, adds items to a list, completes and
reopens them, and tags them. Every list belongs to its creator: another caller's list or item answers
`404` `todoList.notFound` or `todoItem.notFound` exactly as a missing one does
([ADR 0009](../decisions/0009-ownership-is-in-the-query.md)). Once an account is deleted, its lists,
and the items and tags on them, are deleted too
([ADR 0012](../decisions/0012-account-deletion-each-feature-decides.md)).

## How it works

`TodoList` is the aggregate root and owns its `TodoItem`s, so every rule is checked in one place, in
the domain:

- a list name and an item title are required and at most 200 characters once trimmed;
- an item title is unique within its list, ignoring case, and a list holds at most 500 items;
- an item description is optional, trimmed, and at most 2000 characters;
- an item carries at most 20 tags;
- completing an item that is already completed is refused.

A broken rule answers `409` `domain.invariantViolated` with the rule's own message. The repository
loads a list with all its items and tags, a use case changes the aggregate and saves, and the rows live
in the `todo` schema: `TodoLists`, `TodoItems` and `TodoItemTags`. Each operation is one use case in
the application layer, a command or a query; `ITodoListService` loads a list for its owner at the
version the caller sent, which is how every write shares one ownership and version check.

The aggregate raises three events. `TodoItemCompletedDomainEvent` is consumed twice: a consumer in
this feature logs it, and a consumer in [Reminders](reminders.md) cancels the item's pending
reminders, so one event reaches two features without either naming the other.
`TodoListCreatedDomainEvent` and `TodoItemReopenedDomainEvent` have no consumer yet, and
`DomainEventTests` records them as deliberately unconsumed.

Items are never reached without their list, and a write to an item answers with the list. The
version belongs to the list, so one `ETag` guards the list and everything on it.

An MCP server, when this project holds one, offers four tools in its `Features/TodoLists/Tools` folder:
`list_todo_lists` and `get_todo_list`, which need the `read` scope of the caller's credential, and
`create_todo_list` and `add_todo_item`, which need `write`. Completing, renaming, tagging and deleting
are not offered as tools.

## Use it

<!--#if (api)-->
The API serves TodoLists under `/api/v1/todo-lists`, for a signed-in caller. The
[API reference](../reference/api.md#to-do-lists) lists every route with its status; the ones a
client meets first:

| Request | Answers |
|---|---|
| `POST /api/v1/todo-lists` | `201` with the list; `400`; `Idempotency-Key` accepted |
| `GET /api/v1/todo-lists` | `200`, a page of summaries, filtered by `search`, `createdAfter` and `createdBefore`, sorted by `name`, `createdAt` or `lastModifiedAt`, newest first by default; pages of 20, at most 100 |
| `GET /api/v1/todo-lists/{todoListId}` | `200` with the list, its items and tags, and its `ETag`; `304` to a matching `If-None-Match`; `404` |
| `POST /api/v1/todo-lists/{todoListId}/items` | `201`; `409` for a duplicate title or a full list; `Idempotency-Key` accepted |
| `POST /api/v1/todo-lists/{todoListId}/items/{todoItemId}/complete` | `200` with the list; `409` when already completed |
| `GET /api/v1/todo-lists/tags` | `200`, the tags the caller used, through a one-minute cache |

A list and its first item are created like this, then completed with the `ETag` the list last returned:

```bash
API=http://localhost:8080    # http://localhost:5187 on the host

curl -X POST "$API"/api/v1/todo-lists \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" -d '{"name": "Groceries"}'

curl -X POST "$API"/api/v1/todo-lists/$LIST_ID/items \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"title": "Milk"}'

curl -X POST "$API"/api/v1/todo-lists/$LIST_ID/items/$ITEM_ID/complete \
  -H "Authorization: Bearer $TOKEN" -H 'If-Match: "AAAABw"'
```

Every write accepts `If-Match`: a stale version is `412`, and a missing one is `428` when
`Concurrency:IfMatch` is `Required`. A client reads the list again after a `412`, and treats `409` as a
rule of the list it must show the user. [The HTTP API](../architecture/http-api.md#etag-and-if-match)
holds the rules every endpoint shares.

<!--#endif-->
<!--#if (!api)-->
No HTTP route serves TodoLists in this project: a host that holds the application layer sends its use cases for the caller it names.

<!--#endif-->
## Configure

Nothing to configure for TodoLists itself: its limits are constants of the aggregate, the value
objects and `TodoListCollectionPolicy`.
<!--#if (api)-->
The `ETag` precondition and the idempotency of a create read
[`Concurrency`](../reference/configuration.md#concurrency) and
[`Idempotency`](../reference/configuration.md#idempotency).
<!--#endif-->

## Tests

The aggregate, its value objects and its events are tested in
`Tests/Domain/AT.Domain.UnitTests/Features/TodoLists`, each use case, the collection policy and the
account-deletion consumer in `Tests/Application/AT.Application.UnitTests/Features/TodoLists`, and the
mapping between aggregate and rows, with the sort and search queries, in
`Tests/Infrastructure/AT.Infrastructure.Persistence.UnitTests/Features/TodoLists`.
<!--#if (api)-->
`Tests/Integration/AT.Api.IntegrationTests/TodoLists` runs the routes against a database: the rules
of the aggregate, the version precondition, conditional reads, both paginations, auditing and the
dispatch of the domain events. `Tests/Integration/AT.Api.IntegrationTests/Security/OwnershipIsolationTests.cs`
holds the `404` for another caller's data.
<!--#endif-->
An MCP server's own tests for these tools are in its test project, under `Features/TodoLists/Tools`.

## Decisions

- [ADR 0002: two feature shapes](../decisions/0002-two-feature-shapes.md)
- [ADR 0004: features meet through ports and events](../decisions/0004-features-meet-through-ports-and-events.md)
- [ADR 0008: optimistic concurrency end to end](../decisions/0008-optimistic-concurrency-end-to-end.md)
- [ADR 0009: ownership is in the query](../decisions/0009-ownership-is-in-the-query.md)
- [ADR 0012: on account deletion, each feature decides](../decisions/0012-account-deletion-each-feature-decides.md)
