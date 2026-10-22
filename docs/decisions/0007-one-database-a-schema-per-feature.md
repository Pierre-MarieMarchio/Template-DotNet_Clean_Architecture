# 0007. One database, a schema per feature

Status: Accepted

## Context

A modular service can give each module a database, a connection string or a schema. Several
databases mean several things to configure, back up and keep in step, for one process that deploys
as a unit. One database with every table in one schema makes a feature's tables impossible to tell
apart, and removing a feature becomes a hunt for what it created.

## Decision

<!--#if (postgreSql)-->
- **One database, one connection string.** Every context reaches PostgreSQL through the connection
  string named `Default`; a second name would be a second thing that can disagree.
<!--#endif-->
<!--#if (sqlServer)-->
- **One database, one connection string.** Every context reaches SQL Server through the connection
  string named `Default`; a second name would be a second thing that can disagree.
<!--#endif-->
<!--#if (sqlite)-->
- **One database, one connection string.** Every context reaches the SQLite file through the
  connection string named `Default`; a second name would be a second thing that can disagree.
<!--#endif-->
- **A schema per feature.** A feature's tables live in its own schema; tables no feature owns live
  in `platform`. No foreign key crosses two schemas.
<!--#if (tablePrefixes)-->
  This database has no schemas, so `SchemaAsTablePrefixConvention` turns the schema into a table
  name prefix, `<schema>_<Table>`, and each context's history table gets a name of its own.
<!--#endif-->
- **A context per module.** `AppDbContext` holds the business features, and lists each feature's
  configuration one by one rather than scanning for them.
<!--#if (localAuth)-->
  Identity is a module of its own with `AuthDbContext`, the `identity` schema and its own migrations
  history, so accounts and features never share a transaction.
<!--#endif-->
<!--#if (postgreSql)-->
- **The pool is bounded once.** `Database:MaxPoolSize` applies to every context, kept well under
  PostgreSQL's server-wide connection limit.
<!--#endif-->
<!--#if (sqlServer)-->
- **The pool is bounded once.** `Database:MaxPoolSize` applies to every context, kept well under
  SQL Server's connection limit.
<!--#endif-->
<!--#if (sqlite)-->
- **The pool is not bounded.** SQLite has no server-wide connection limit, so `Database:MaxPoolSize`
  has no effect.
<!--#endif-->

## Consequences

- A feature can be removed by deleting its code and its migrations, and moved to a database of its
  own later by changing where its context connects, because nothing joins across schemas.
- A write that spans two contexts is two transactions. Work that must follow a change in another
  context reacts to an event and re-derives its precondition
  ([ADR 0006](0006-events-after-commit-without-an-outbox.md)).
- `PersistenceModelTests` checks that every configuration is applied by the context and declared in
  its module.
<!--#if (serverDatabase)-->
- The pool size is multiplied by the number of API instances; the deployment sizes it against the
  server's limit.
<!--#endif-->

[One database, a schema per feature](../architecture/persistence-model.md#one-database-a-schema-per-feature)
names the schemas and contexts, and [Migrations](../guides/migrations.md) the migrations per context.

## Revisit when

A feature needs isolation that a schema does not give: its own scaling, backup policy or
compliance boundary.
