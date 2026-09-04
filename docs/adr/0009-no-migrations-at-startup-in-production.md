# 0009 — Migrations are not applied at startup outside Development

Status: Accepted

## Context

The previous host migrated and seeded on **every start, in every environment, before
Kestrel began listening** — via two sequential hand-rolled retry loops of up to twenty
seconds each, with no `try`/`catch`. A bad connection string therefore produced a
forty-second silent hang followed by an unhandled exception, and the same code path also
seeded an `admin`/`admin` account outside its own environment guard.

Even without those defects, migrate-on-startup has structural problems in a deployed
environment:

- The application's runtime credentials need **DDL rights permanently**, so a
  compromised process can drop tables, not merely read them.
- With more than one replica, several instances race on `__EFMigrationsHistory` on a cold
  start. EF takes a lock, so the usual outcome is a slow start rather than corruption —
  but it is a lock contended by processes that are simultaneously trying to serve traffic.
- Schema change becomes a side effect of "whichever instance boots first" instead of a
  deliberate, reviewable, revertible step.
- A failed migration takes the application down with it, and rolling back the deployment
  does not roll back the schema.

## Decision

Startup applies migrations **only in Development**:

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    await app.MigrateAndSeedForDevelopmentAsync();
}
```

`MigrateAndSeedForDevelopmentAsync` runs after the app is built, inside a scope, logs
failures with context and **rethrows** — a development environment that cannot reach its
database should fail loudly and immediately, not start and fail one request at a time.
Transient unavailability is Npgsql's job: `EnableRetryOnFailure` replaces the hand-rolled
loops.

Identity seeding is separately gated on `IdentitySeed:Enabled` and **throws** if enabled
outside Development ([see CONFIGURATION.md](../CONFIGURATION.md#identityseed--development-only)).

In every other environment, apply migrations as an explicit deployment step:

```bash
dotnet ef database update --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
                          --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
```

or build a migration bundle in CI and run that:

```bash
dotnet ef migrations bundle --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
                           --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
```

There is one context and one history table (in the connection's default schema), so this is a single step — see [0010](0010-one-persistence-project-one-dbcontext.md). It used to be two, which meant a deployment could leave one feature's schema ahead of the other's.

## Consequences

- Production runtime credentials need only DML. DDL rights belong to the migration step's
  identity, which exists for the duration of the deployment.
- Schema change is a reviewable, revertible artefact with its own logs, run once rather
  than once per replica.
- A migration failure fails the deployment before new pods take traffic, instead of
  crash-looping the application.
- **`git clone && dotnet run` still just works**, which is the property that makes
  migrate-on-startup tempting in the first place. It is kept exactly where it is harmless.
- The cost: the deployment pipeline now has a step it did not have, and there is a window
  where code and schema versions differ. That is a real operational obligation, and it
  forces the discipline that makes zero-downtime deployment possible at all — migrations
  must be backward-compatible with the previous application version (expand, deploy,
  contract).
- Someone will eventually forget the step and see a runtime error about a missing column.
  `/health/ready` checks the database, so wire it to your readiness gate — that is the
  cheapest place to catch it.
- `dotnet ef migrations bundle` is documented here but **was not run** during this
  change; the `database update` form and `migrations list` were.

## Alternatives rejected

- **Migrate on startup everywhere** (what was there). Permanent DDL rights, replica
  races, and schema change as an accident of boot order.
- **Migrate on startup guarded by a leader election or advisory lock.** Solves the race,
  keeps the DDL rights and the coupling of schema change to process start.
- **A separate init container or Kubernetes Job running `database update`.** This is a good
  answer, and it is exactly what "an explicit step" means — the ADR does not prescribe
  where the step lives, only that it is not inside the serving process.
- **`EnsureCreated()`.** No migration history at all, so there is no path to the second
  schema version. Acceptable only for a throwaway test database.
- **Idempotent SQL scripts generated at release time** (`migrations script --idempotent`).
  Entirely reasonable, and preferable where a DBA must review the SQL before it runs.
  Compatible with this decision.

## Revisit when

Never for production. The Development convenience is the part that could go: if the team
adopts a `docker compose run migrate` step locally, the `if (IsDevelopment())` block
becomes dead weight and should be deleted rather than left as a second, divergent path to
the same schema.
