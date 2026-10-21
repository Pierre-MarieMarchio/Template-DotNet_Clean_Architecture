# Persistence model

This project stores everything in one PostgreSQL database through EF Core. EF maps row records,
never the domain types; a use case commits once through `IUnitOfWork`; and every write that names a
version is refused when the row has moved since. Adding, checking and applying migrations is
[the migrations guide](../guides/migrations.md).

## One database, a schema per feature

Every context reaches the database through the one connection string `ConnectionStrings:Default`.
A feature's tables live in its own schema, so a feature is removed with its migration rather than by
editing a shared model, and the tables no feature owns live in `platform`.

| Schema | Tables | Context |
|---|---|---|
<!--#if (todoLists)-->
| `todo` | `TodoLists`, `TodoItems`, `TodoItemTags` | `AppDbContext` |
<!--#endif-->
<!--#if (reminders)-->
| `reminders` | `Reminders` | `AppDbContext` |
<!--#endif-->
<!--#if (files)-->
| `files` | `StoredFiles`, `StoredFileTags`, `ContentSweepCheckpoints` | `AppDbContext` |
<!--#endif-->
<!--#if (notes)-->
| `notes` | `Notes` | `AppDbContext` |
<!--#endif-->
<!--#if (externalAuth)-->
| `external_identity` | `ExternalIdentities`, `RoleAssignments` | `AppDbContext` |
<!--#endif-->
| `platform` | `IdempotencyKeys` | `AppDbContext` |
| `public` | `__EFMigrationsHistory` of the business context | `AppDbContext` |
<!--#if (localAuth)-->
| `identity` | `User`, `Role`, `UserRoles`, `UserClaims`, `UserLogins`, `RoleClaims`, `UserTokens`, `RefreshTokens`, `RefreshTokenFamilies`, `UserPasskeys`, `SpentPasskeyCeremonies`, `SecurityEvents`, `DataProtectionKeys`, and its own `__EFMigrationsHistory` | `AuthDbContext` |
<!--#endif-->

Each context declares its schema names as constants, and every table names its schema in its own
configuration, so no default schema is set and a mapping cannot drift into the wrong one by omission.

<!--#if (localAuth)-->
The identity tables have a context of their own, `AuthDbContext`, with its own migrations history.
The authentication half migrates without the business half's permission and is removed by deleting a
project. Two contexts are two units of work, so no transaction spans an identity write and a business
write; no code path needs one.
<!--#endif-->

No foreign key crosses from a business table to an account. A record's `OwnerId` is a bare `Guid`
column, and an aggregate's owner is a `UserId` in a project that knows neither EF Core nor any
identity provider.

All contexts built on the one connection string share one Npgsql pool, bounded once by
`Database:MaxPoolSize`; [the configuration reference](../reference/configuration.md) says how to size
it against replicas.

<!--#if (aggregates)-->
## Rows are not aggregates

A rich feature's aggregate is mapped onto `…Record` rows by a mapper, and a tracker remembers which
aggregates a request loaded or added. The aggregate answers only to its own rules and the row only to
the schema; the price is paid in one place, the mapper.

The mapper is total in one direction, so everything stored becomes domain state. Onto a tracked row
it writes the domain's columns and leaves the version and the audit stamps alone, so EF's own diff
decides what to write. A mapper that forgets a property loses data without failing anything, so a
round-trip test that walks the aggregate's type is the barrier.

<!--#endif-->
<!--#if (tagging)-->
Child rows are entities with composite keys, not owned collections: a tag is keyed by its owner and
its value, so a duplicate is impossible in the database, and reconciliation is a set difference.
<!--#endif-->

<!--#if (notes)-->
## Stores

A simple feature has no mapper, no tracker and no aggregate. Its store loads the owner's row tracked,
checks the caller's version against it, and stages the edit on the row itself. The same unit of work
commits it, and the save interceptors find nothing of it to flush, stamp or publish.
<!--#endif-->

## The save pipeline

Three `SaveChanges` interceptors run, in the order the context attaches them:

| Interceptor | Does |
|---|---|
| Aggregate flushing | writes each tracked aggregate onto its rows, before EF computes its diff |
| Audit stamping | stamps `CreatedAt`, `CreatedBy`, `LastModifiedAt`, `LastModifiedBy` on what changed |
| Domain event dispatch | collects the staged aggregates' events, and publishes them after the commit |

They are interceptors rather than steps of the unit of work, so a stray `SaveChanges()`, the
synchronous one included, flushes and publishes too. The business context attaches all three with
`UseAggregateFlushing`, `UseAuditStamping` and `UseDomainEventDispatch`.
<!--#if (localAuth)-->
The identity context attaches none: it has no aggregate and no audited row.
<!--#endif-->

Stamps record the actor `IAuditActor` names: the caller in a request, nobody in a background job.
An unattributed write stores null, never `Guid.Empty`.

## Unit of work and the commit boundary

The use case owns the commit, and nothing else may make one. A repository or a store stages;
`IUnitOfWork.SaveChangesAsync` commits; EF wraps that one save in a transaction, so what the use case
staged lands whole or not at all.

The commit boundary is one save of one named context. A module that owns a context registers
`IContextUnitOfWork<TContext>` for it, and the business module registers the unnamed `IUnitOfWork`
over its own. An untyped boundary could commit one context's writes through another and report
success with zero rows.

Some writes are not the request's to own, and commit on a context of their own: an idempotency claim
must survive whatever the use case that follows does.
<!--#if (externalAuth)-->
So does the mapping of a provider's subject to its owner, written while the request is
authenticated: a failed operation must not roll back a mapping that is true regardless.
<!--#endif-->
<!--#if (localAuth)-->
ASP.NET Core Identity's stores commit as they return, and issuing, rotating or revoking a refresh
token commits the request's unit of work, because a grant must be durable before its value leaves
the process. A refresh-token rotation is one transaction of its own, replayed whole on a transient
failure.
<!--#endif-->

## Deleting removes rows

`DELETE` removes rows; there is no deleted flag. A flag makes every query carry a predicate that one
forgotten filter turns into a data leak, and retention is a question for an audit trail.
`NoPersistenceRecord_CarriesADeletedFlag` holds it.

## Concurrency

The version of a row is PostgreSQL's `xmin`: never written by this process, read by the same query
that reads the value, and published as the resource's `ETag`. A write puts the version it loaded in
its `WHERE` clause, so a writer that loses a race between its load and its commit matches no row.
`EfUnitOfWork` turns EF's conflict into `ConcurrencyConflictException`, and the API answers `409`
`concurrency.conflict`. Nothing retries it: re-applying a decision made against a state that no
longer exists is the lost update the version prevents.

When the caller sends `If-Match`, the use case compares it with the version it just loaded, after
the ownership check, and answers `412` when they differ. [HTTP API](http-api.md#etag-and-if-match)
states the client's side.

<!--#if (accounts)-->
Ownership is part of the read: a query names the owner in its predicate, so another user's row never
leaves the database and is answered exactly as a missing one. A command that loads an aggregate by id
compares the owner in one place per feature, and answers the same `404`.
<!--#endif-->

## Decisions

- [0005. The domain is not mapped by EF](../decisions/0005-the-domain-is-not-mapped-by-ef.md)
- [0007. One database, a schema per feature](../decisions/0007-one-database-a-schema-per-feature.md)
- [0008. Optimistic concurrency end to end](../decisions/0008-optimistic-concurrency-end-to-end.md)
<!--#if (accounts)-->
- [0009. Ownership is in the query](../decisions/0009-ownership-is-in-the-query.md)
<!--#endif-->
