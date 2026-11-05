---
type: decision
title: 0038. Deleting removes rows
description: Why a deletion removes the row and its children, and no record carries a deleted flag.
status: Accepted
---

# 0038. Deleting removes rows

Status: Accepted

## Context

A `DELETE` can remove the row, or set a flag that hides it: an `IsDeleted` column, or a `DeletedAt`
stamp, with a global query filter so that reads skip it. The flag keeps the data for an undo, an
audit or a support request, and is a common default in .NET projects through EF Core's query
filters. It also makes every read responsible for the filter: a raw query, a projection that
bypasses the filter, a unique index that still counts the hidden row, a join through another feature
or a report written by hand each serve the data that was meant to be gone.
<!--#if (accounts)-->
Ownership is already a predicate in every query
([ADR 0009](0009-ownership-is-in-the-query.md)); a second predicate that one forgotten line drops is
a second way to leak.
<!--#endif-->

## Decision

- **A deletion removes the row, and its children with it.** No persistence record carries a deleted
  flag; `StorageShapeTests.NoPersistenceRecord_CarriesADeletedFlag` refuses a property named as one
  (`IsDeleted`, `DeletedAt` and the other names the test lists) on every record of the persistence
  module.
- **What must outlive a deletion is written elsewhere**: an audit trail, or a history table the
  deletion writes to, which the feature's reads never query.

## Alternatives considered

- **A deleted flag with a global query filter.** An undo for free, and a filter every raw query,
  index and report must remember; the one that forgets serves deleted data.
- **A flag without a filter, checked by each query.** The same leak, with more places to forget it.
- **SQL Server temporal tables, or a history table kept by triggers.** History without a flag, tied
  to one provider and invisible to the code that reasons about it; this project carries one provider
  per generation and keeps its behaviour in code.

## Consequences

- A deleted record is gone: an undo, a recycle bin or a restore by support is a feature to build,
  with its own state and its own storage.
- Unique constraints and counts mean what they say, with no hidden row in them.
- A feature that needs to keep what it deletes chooses where, and says so on its page.

## When this decision does not apply

- **A business state that hides a record**, such as archived, cancelled or completed, is a state of
  the aggregate, named for what it means, with the rules the domain gives it; it is not a deletion,
  and lists filter it as they filter any other state.
<!--#if (anyFeature && accounts)-->
- **An account's data after the account is deleted** follows each feature's declared policy, delete
  or anonymise, as [ADR 0012](0012-account-deletion-each-feature-decides.md) has it.
<!--#endif-->

[Deleting removes rows](../architecture/persistence-model.md#deleting-removes-rows) is the rule as
the persistence model states it.

## Revisit when

A regulation or a customer requires deleted records to be restorable for a period, for most of the
features at once, or the project moves to a store that keeps history natively.
