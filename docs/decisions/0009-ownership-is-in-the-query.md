# 0009. Ownership is in the query

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
never a `403`. `OwnedAggregate` answers both cases alike for an aggregate, with the error the
feature passes in.

Each feature has one query that crosses owners on purpose: the owner ids that still hold data, for
the account-deletion sweep. It returns ids and nothing else.

The generator's `--access owner` writes a feature this way; `--access shared` writes one whose
records every authenticated caller sees, with no owner at all.

## Consequences

- No route discloses whether an id exists, and no code path holds another user's row.
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
