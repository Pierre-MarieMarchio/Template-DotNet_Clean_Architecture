# 0021. SQLite is a development database

Status: Accepted

## Context

This project runs on SQLite: one file, no server to install, nothing to start before the API. That
is what makes it quick to clone and run, and it is also what SQLite cannot give a deployed service.
A file has one writer at a time, its locks live in the process that opened it, and its types are
fewer than a server's. A project that starts anywhere would find these limits in production, under
load, rather than on the first start.

## Decision

- **The API starts in Development only.** At composition, before any service is registered, the
  host throws when its environment is not `Development`, with a message that names the way out.
  No setting overrides it. The integration suites run the API in `Development` on a file of their
  own, and a test starts it in `Production` and `Staging` and sees it refused.
- **One file, one process.** The connection string names a file under App_Data, which the API
  creates on its first connection, switched to write-ahead logging so that readers do not wait for
  the writer. A write that finds the file busy waits up to the command timeout, and a save that
  still finds it busy is retried.
- **Leases are locks of the process.** `ILeaderLease` and the owner's quota lock are semaphores
  inside the API ([ADR 0011](0011-jobs-run-in-the-api-under-a-lease.md)): they exclude every caller
  of one process and nothing beyond it, which is all one file allows anyway.
- **What the server types do, conventions do.** A table is prefixed with its feature's schema, since
  SQLite has none ([ADR 0007](0007-one-database-a-schema-per-feature.md)); the row version is a
  counter the save stamps ([ADR 0008](0008-optimistic-concurrency-end-to-end.md)); every
  `DateTimeOffset` is stored as a number, so that the database can compare and order it.

## Alternatives considered

- **Let it start anywhere and document the limits.** A limit that is only written down is found in
  production; the refusal finds it on the first start.
- **A setting that lifts the refusal.** Every deployment would set it, and the refusal would stop
  saying anything.
- **Refuse to generate SQLite at all.** A server database in a container is the right default, and it
  stays one; SQLite is for the developer who wants none.

## Consequences

- Concurrent writers queue on the file: a burst of writes is served one after the other, and a
  write that waits longer than the command timeout fails.
- A second instance of the API would hold its own leases and its own quota locks, so two instances
  would run the same job pass and could both accept a file past the quota. The refusal outside
  Development keeps a second instance out of reach.
- Search folds case for ASCII letters only: `Élan` does not find `élan`.
- A `DateTimeOffset` keeps a tenth of a millisecond: a value read back can differ from the one
  written below that.
- No release workflow and no migration bundle are generated: there is nothing to deploy.

To deploy, generate the project again with `--database postgresql`, `sqlserver` or `mysql`, move
the features' code across, and let `bootstrap` create the migrations for the new provider; the
domain and the application do not change, since no provider reaches them.

## Revisit when

SQLite gains concurrent writers, or a deployment of one instance on one disk becomes a shape this
project supports.
