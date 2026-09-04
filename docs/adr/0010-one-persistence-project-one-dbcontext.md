# 0010 — One persistence project, one DbContext

Status: Accepted — supersedes [0006](0006-two-dbcontexts-one-database.md), amends [0007](0007-module-per-capability-infrastructure.md)

## Context

[0006](0006-two-dbcontexts-one-database.md) chose two `DbContext`s on one database, and
[0007](0007-module-per-capability-infrastructure.md) chose one infrastructure project per
capability. Together they produced three projects that all did persistence:
`AppTemplate.Infrastructure.Persistence` (mechanics), `AppTemplate.Infrastructure.TodoLists` (a context, a
schema, migrations) and `AppTemplate.Infrastructure.Identity` (another context, another schema,
another set of migrations).

The stated benefit of that shape was independent migratability: each module could be
migrated from its own project without the others. In practice it bought:

- Two `__EFMigrationsHistory` tables, so "is the schema current" had two answers and a
  deployment could leave one feature's schema ahead of the other's.
- Every `dotnet ef` command needing `--context`, and a base `AppDbContext` that was never a
  valid argument to it.
- No transaction spanning an identity write and a domain write, which is why refresh-token
  rotation committed on its own and `IUnitOfWork` had a documented exception.
- One `IUnitOfWork` binding that exactly one module was allowed to claim, enforced by a
  comment.

Nothing ever exercised the independence. Both contexts were migrated in sequence by the
same start-up routine, applied in sequence by the same test fixture, and pointed at the
same connection string.

## Decision

**All persistence lives in `AppTemplate.Infrastructure.Persistence`, behind one `AppDbContext`, with
one migrations history.**

```
AppTemplate.Infrastructure.Persistence/
  Common/       Contexts/ Auditing/ DomainEvents/ Mapping/ Time/ UnitOfWork/
  Features/     TodoLists/{Models,Configurations,Mappers,Tracking,Repositories,Queries,DomainEvents}
                Identity/{Models,Configurations,Stores,Seeding}
  Migrations/   one history, in the connection's default schema
  PersistenceModule.cs
```

`AppDbContext` derives from `IdentityDbContext<AppUser, AppRole, Guid>` and also maps the
to-do list feature's rows. The features stay separated by **schema** — `identity` and
`todo` — named table by table in each feature's `IEntityTypeConfiguration`, so no default
schema is set and a new mapping cannot land in the wrong schema by omission.

`AppTemplate.Infrastructure.TodoLists` is deleted. `AppTemplate.Infrastructure.Identity` keeps everything that
makes a decision — password and lockout policy, bearer validation, access-token issuance,
refresh-token rotation — and has no EF provider reference, no migrations and no design-time
factory.

## Consequences

- **A real transaction across features.** `RefreshTokenService` now stages through
  `IRefreshTokenStore` and commits through `IUnitOfWork`, the same one a domain write uses.
- **One answer to "is the schema current."** One history table, one `database update`, one
  `has-pending-model-changes`, and no `--context` anywhere.
- `IUnitOfWork` has one obvious implementation instead of a generic one plus a rule about
  who may bind it. `UnitOfWork<TContext>` became `EfUnitOfWork`, which also removed the
  namespace/type collision (CS0307) the old name required a call-site workaround for.
- **The commit boundary is where provider exceptions stop.** `EfUnitOfWork` translates
  `DbUpdateConcurrencyException` into `ConcurrencyConflictException`, so EF appears in
  neither Application nor Presentation, and a lost update is a `409` with the stable code
  `concurrency.conflict`.
- **ASP.NET Identity's own stores are still outside the boundary.** `UserManager.CreateAsync`
  commits before it returns. That is framework behaviour, so registration remains explicitly
  non-atomic and the resend endpoint remains necessary.
- **The persistence project now holds more than one capability**, which is a real departure
  from 0007's one-project-per-capability rule. It is partitioned internally instead, and an
  architecture test asserts what the folders claim: nothing under `Common/` may name a
  feature, with `AppDbContext` as the single documented exception, because it applies every
  feature's configuration and is therefore the model's composition root.
- **A schema change to one feature now rebuilds the other's project.** Accepted: the same
  was already true of every change to the shared mechanics.
- The Dockerfile's `COPY` list is one line shorter, and the health check is one check
  instead of two — two checks over one connection reported the same fact twice and could not
  disagree.

## Alternatives rejected

- **Keep two contexts inside one project.** Keeps the two histories, the `--context`
  arguments and the missing transaction, and gives up the only thing they bought (project
  independence) anyway. The worst of both.
- **Keep three projects and merge only the contexts.** A context cannot span projects
  without one of them referencing the other's entity types, which is the horizontal
  dependency [0007](0007-module-per-capability-infrastructure.md) forbids.
- **A `DbContext` per feature over a shared model.** EF has no such concept; two contexts
  over the same tables means two models to keep in agreement, with no mechanism forcing
  them to agree.
- **Separate databases.** Two connection strings, two backup schedules, two failure domains
  and still no cross-database transaction. All of the cost of separation, none of the
  convenience of one instance.

## Revisit when

Identity needs to scale or be secured independently — different credentials, different
backup retention, different blast radius. The absence of any foreign key or navigation from
the domain to `AppUser` is what keeps that a configuration change rather than a rewrite; see
[0011](0011-persistence-models-separate-from-the-domain.md) for why that absence is now
structural rather than a matter of discipline.
