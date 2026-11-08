---
type: decision
title: 0004. Ownership is in the query
description: Why the owner is in every query's predicate, so another caller's record answers 404 exactly as a missing one.
status: Accepted
---

# 0004. Ownership is in the query

Status: Accepted

## Context

A feature whose records belong to a caller must keep everyone else out. The common way is to load
the record by its id and then compare its owner with the caller. That reads another user's row into
memory first, and it answers "not yours" (`403`) differently from "not there" (`404`): a way for
anyone to ask whether an id names something real.

## Decision

The owner is in every predicate. Each query and each write of an owner-scoped feature filters by
the caller's id in the same statement that finds the record, and a record that belongs to someone
else is answered exactly as one that does not exist: the feature's own not-found error, a `404`,
never a `403`. An aggregate is no exception: its repository loads one by its owner and its id
together, `GetAsync(ownerId, id, cancellationToken)`, the adapter puts both in the `WHERE` clause,
and an aggregate the unit of work already holds is handed back under the same predicate. The use
case passes the caller and answers its not-found error when nothing comes back, so no aggregate of
another user is ever loaded to be refused.
<!--#if (files)-->
A file that a category opens to every signed-in caller is read through
`GetReadableAsync(readerId, id, categories)`, whose predicate names the reader too: the reader's own
file, or an available one in a category the configuration opens.
<!--#endif-->

The reads that cross owners on purpose have no caller behind them and a name of their own: the
owner ids that still hold data, for the account-deletion sweep, which returns ids and nothing else,
and the batches a background pass works through.

The generator's `--access owner` writes a feature this way; `--access shared` writes one whose
records every authenticated caller sees, with no owner at all.

## Consequences

- No route discloses whether an id exists, and no request loads another user's row to refuse it.
- `OwnedAggregateLoadTests` holds the shape: every domain port that returns one `IOwnedAggregate`
  takes the owner's `UserId`.
<!--#if (api && todoLists)-->
- `TodoListOwnerQueryTests` runs the to-do list repository's predicate against SQLite, where a
  stranger's read finds nothing and leaves the context empty.
<!--#endif-->
<!--#if (api && files)-->
- `StoredFileOwnerQueryTests` runs both reads of the stored-file repository against SQLite.
<!--#endif-->
- A version check runs on the record the owner query found, so a stale caller is never told about
  a record that is not theirs.
- The index behind each sortable field leads with the owner id, which every read filters by.
- A test of a disclosure asserts the whole response, compared with the answer for an absent id.
<!--#if (todoLists)-->
  `OwnershipIsolationTests` does it for to-do lists.
<!--#endif-->
<!--#if (notes)-->
  `NoteOwnershipTests` does it for notes.
<!--#endif-->
<!--#if (files)-->
  `StoredFileOwnershipTests` does it for files.
<!--#endif-->

[Authorisation](../architecture/request-pipeline.md#authorisation) places the rule in the request's
path, and [Generate it](../guides/adding-a-feature.md#generate-it) shows `--access`.

## Revisit when

Records come to be shared between users, through teams, tenants or delegation: ownership then
becomes a membership predicate, still written in the query.
