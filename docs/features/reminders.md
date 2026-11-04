# Reminders

How this project reminds a caller of a to-do item at a chosen moment, by mail, and what it does when
the mail cannot be sent. Reminders is the example of a flat aggregate that a timer drives.

## What you get

A signed-in caller schedules a reminder for one of their to-do items, lists an item's reminders,
reschedules a pending one and cancels one. When the reminder falls due, this project mails the owner
once. A reminder belongs to its creator: another caller's reminder answers `404` `reminder.notFound`
exactly as a missing one does ([ADR 0009](../decisions/0009-ownership-is-in-the-query.md)). Once an
account is deleted, its reminders are deleted too
([ADR 0012](../decisions/0012-account-deletion-each-feature-decides.md)).

A reminder is in one of three states, and a client must handle all three:

| `state` | Meaning |
|---|---|
| `Pending` | waiting for its due date; the only state that can be rescheduled |
| `Fired` | the mail was sent; terminal, and it can no longer be cancelled |
| `Cancelled` | the owner cancelled it, the item was completed or deleted, or the owner's account is gone |

## How it works

`Reminder` is an aggregate of its own, apart from [to-do lists](todo-lists.md): it names an item by
the ids of its list and of the item, and asks `ITodoItemQueries`, a port TodoLists implements, whether
that item exists, belongs to the caller and is completed. `AddReminders()` therefore needs
`AddTodoLists()` in the same host ([modules and boundaries](../architecture/modules-and-boundaries.md)).
The rows live in the `reminders` schema, in the `Reminders` table.

Scheduling an item that is missing or another caller's answers `404` `reminder.targetNotFound`, and a
due date that is not in the future answers `409` `domain.invariantViolated`. The due date is stored in UTC.
Rescheduling clears the failed attempts the reminder had counted.

Two consumers keep the state true without a call from the caller. When a to-do item is completed,
`CancelRemindersOnTodoItemCompletedConsumer` cancels its pending reminders. When an account is
deleted, `DeleteRemindersOnAccountDeletedConsumer` removes the owner's reminders.

An MCP server, when this project holds one, offers two tools in its `Features/Reminders/Tools` folder:
`list_reminders`, which needs the `read` scope of the caller's credential, and `schedule_reminder`, which
needs `write`. Rescheduling and cancelling are not offered as tools.

### The pass that sends them

`FireDueReminders` is the pass. The host that runs the jobs runs it on a timer: `ReminderJobsExtensions`,
in that host's `Features/Reminders/Jobs` folder, registers it, as [background jobs](background-jobs.md)
describes for every job and says which host that is.
It holds the lease `apptemplate.reminders.fire-due`, so only one instance runs a pass at a time, and
each pass takes at most 200 pending reminders whose due date and whose next attempt have come, the
oldest first. For each one:

1. If the item no longer exists or is completed, the reminder is cancelled.
2. If its owner has no account to notify, the reminder is cancelled and a warning is logged.
3. Otherwise `IReminderNotifier` sends the mail. On success the reminder becomes `Fired` and raises
   `ReminderFiredDomainEvent`.
4. If sending fails, the reminder counts the failure and is not tried again before a delay that
   starts at one minute, doubles on each failure and stops growing at one hour
   (`Src/Domain/AT.Domain.Core/Common/Retries/RetryBackoff.cs`). It stays `Pending`.

A reminder changed by someone else while a pass fires it ends the pass, and the next one reads it
afresh. `ReminderFiredDomainEvent` has no consumer, and `DomainEventTests` records it as deliberately
unconsumed.

The address comes from `IReminderRecipientDirectory`, which each host that runs the pass implements
in its `Features/Reminders/Composition` folder:
<!--#if (localAuth)-->
the recipient is the owner's local account, and its address is the account's email.
<!--#endif-->
<!--#if (externalAuth)-->
the recipient is the owner's mapped identity, and its address is the email the identity provider
gave; an owner whose provider gave none has nobody to notify.
<!--#endif-->
The mail itself is `EmailReminderNotifier`, in `Src/Infrastructure/AT.Infrastructure.Email/Features/Reminders`,
over the transport [email](email.md) describes.

## Use it

<!--#if (api)-->
The API serves Reminders for a signed-in caller, under the item for listing and scheduling, and under
`/api/v1/reminders` for a change. The [API reference](../reference/api.md#reminders) lists the routes.

| Request | Answers |
|---|---|
| `GET /api/v1/todo-lists/{todoListId}/items/{todoItemId}/reminders` | `200`, the caller's reminders for the item, cancelled ones included; an unknown or foreign item answers an empty list, not `404`, since a reminder outlives its item |
| `POST /api/v1/todo-lists/{todoListId}/items/{todoItemId}/reminders` | `201`; `404` `reminder.targetNotFound`; `409` for a due date in the past; `Idempotency-Key` accepted |
| `PUT /api/v1/reminders/{reminderId}` | `200`; `404`; `409` when the reminder is no longer pending; `412`; `428` |
| `DELETE /api/v1/reminders/{reminderId}` | `204`; `404`; `409` when the reminder has fired; `412`; `428` |

```bash
API=http://localhost:8080    # http://localhost:5187 on the host

curl -X POST "$API"/api/v1/todo-lists/$LIST_ID/items/$ITEM_ID/reminders \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" -d '{"dueAt": "2030-01-01T09:00:00Z"}'
```

A client shows a `409` as the reminder's own state, not as a failure to retry. `If-Match` follows
[the HTTP API](../architecture/http-api.md#etag-and-if-match).

<!--#endif-->
<!--#if (!api)-->
No HTTP route serves Reminders in this project: a host that holds the application layer sends its use cases for the caller it names.

<!--#endif-->
## Configure

| Setting | Effect |
|---|---|
| [`Jobs:Reminders`](../reference/configuration.md#jobsreminders) | the pass's interval, one minute by default, and its switch |
| [`Email`](../reference/configuration.md#mail) | the transport of the mail and its settings |

With `Jobs:Reminders:Enabled` off, or `Jobs:Enabled` off, no reminder is ever sent: they stay `Pending`.

## Tests

The aggregate is tested in `Tests/Domain/AT.Domain.UnitTests/Features/Reminders`, each use case, the
two consumers and the pass, with its failure, retry and conflict cases, in
`Tests/Application/AT.Application.UnitTests/Features/Reminders`, and the mapping between aggregate and
rows in `Tests/Infrastructure/AT.Infrastructure.Persistence.UnitTests/Features/Reminders`.
The registration of the pass and its settings are tested in the test project of the host that runs the
jobs, under `Features/Reminders/Jobs`, and the recipient directories in the test project of each host
that has one, under `Features/Reminders/Composition`.
<!--#if (api)-->
`Tests/Integration/AT.Api.IntegrationTests/Reminders` runs the routes and a whole pass against a
database: the lifecycle, another caller's reminder and the pass that fires what is due.
<!--#endif-->
An MCP server's own tests for these tools are in its test project, under `Features/Reminders/Tools`.

## Decisions

- [ADR 0004: features meet through ports and events](../decisions/0004-features-meet-through-ports-and-events.md)
- [ADR 0006: events after commit, without an outbox](../decisions/0006-events-after-commit-without-an-outbox.md)
- [ADR 0009: ownership is in the query](../decisions/0009-ownership-is-in-the-query.md)
- [ADR 0012: on account deletion, each feature decides](../decisions/0012-account-deletion-each-feature-decides.md)
