# Migrations

**Two `DbContext`s, two migration histories, one database.** One connection string, one pool, five
schemas. Every command below has to name which of the two it is about, and that is the one thing to
get right here.

| Context | Project | Schemas | History table |
|---|---|---|---|
| `AppDbContext` | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence` | `todo`, `reminders`, `files`, and `platform` for what belongs to no feature | `public.__EFMigrationsHistory` |
| `AuthDbContext` | `Src/Infrastructure/AppTemplate.Infrastructure.Auth` | `identity` | `identity.__EFMigrationsHistory` |

Each ships **one** initial migration, and they are independent: a change to one is invisible to the
other. Features separate themselves by schema, declared table by table in each feature's
`IEntityTypeConfiguration` — not by a context each.

- [The tool](#the-tool)
- [Adding a migration](#adding-a-migration)
- [Checking a migration against the model](#checking-a-migration-against-the-model)
- [Applying them](#applying-them)
- [When `dotnet ef` refuses to run](#when-dotnet-ef-refuses-to-run)

## The tool

`dotnet-ef` is pinned as a local tool, so use the manifest rather than a global install:

```bash
dotnet tool restore
dotnet ef --version      # 10.0.11
```

Both design-time factories read `ConnectionStrings__Default` from the **environment**, with a
localhost fallback. Neither needs a running host, and the drift check below needs no database at
all.

```bash
# bash
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=appuser_local_dev"
```

```powershell
# PowerShell
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=appuser_local_dev"
```

## Adding a migration

For the business half, the launcher is the short form:

```bash
dotnet run Tools/Tasks.cs migration-add <Name>
```

**The launcher's migration tasks target the business context only.** For the authentication half,
name the project yourself:

```bash
dotnet ef migrations add <Name> \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Auth \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Auth \
  --output-dir Migrations
```

The same shape with `Src/Infrastructure/AppTemplate.Infrastructure.Persistence` on both switches is
what `migration-add` prints and runs. No `--context` argument is needed anywhere: each project holds
exactly one context, so naming the project names the context.

`dotnet ef migrations list` and `dotnet ef migrations remove` take the same two switches.
`remove` only undoes a migration that has not been applied.

**An empty migration means the model edit is missing, not that there was nothing to do.** A new
entity reaches the model through three edits in the context — its schema constant, its `DbSet`, and
its `builder.ApplyConfiguration(new …())` call — and a configuration nobody names is inert while
every gate stays green. `PersistenceModelTests.EveryEntityTypeConfiguration_IsAppliedByTheContext`
is the guard; [`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md) is where those three edits sit in the
walkthrough.

## Checking a migration against the model

`has-pending-model-changes` compares the snapshot against the model and needs no database. **Run it
for both contexts** — which is what `dotnet run Tools/Tasks.cs verify` does:

```bash
dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence

dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Auth \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Auth
```

Checking one and not the other is worse than checking neither: the half that is checked goes on
applying cleanly while the other half's tables are simply absent.

`PendingModelChangesTests` proves the same thing from inside the suite, over both contexts, which is
how CI catches drift — the workflow runs no `dotnet ef` command of its own. The integration suites
are the only thing that ever executes an `Up()`.

## Applying them

**In Development, and only in Development, startup applies both.** Authentication first, because
seeding an account writes to its tables; a failure on the second is rethrown rather than stepped
over, since between the two calls one schema exists and the other does not.

**Any other environment starts without touching the schema, and that is deliberate.** Migrating
from the process that serves requests needs DDL rights at runtime and races every replica against
every other on the history table. A deployment applies them as its own step, with a
migration-time principal that has DDL rights and is *not* the application's runtime user:

```bash
dotnet ef database update --project <the project> --startup-project <the same project>
```

or from a self-contained bundle, which is what a deployment should prefer because it carries its
own runtime and needs no SDK on the target:

```bash
dotnet run Tools/Tasks.cs migration-bundle      # both contexts
```

**One bundle per context, and you need both.** `migration-bundle` and
`.github/workflows/release.yml`'s `migration-bundle` job each build two: `efbundle-identity` for the
`identity` schema and `efbundle` for the business one. Applying one and not the other leaves half
the schema absent — an API whose identity tables do not exist starts and then fails the first
sign-in. Their order is not a correctness requirement, since no foreign key crosses the two schemas,
but both run authentication first, as the Development bootstrap does.

**The limit that remains.** `deploy/kubernetes/migration-job.yaml` assumes an image around the two
bundles that the release workflow uploads as plain artifacts rather than packaging; the manifest runs
`efbundle-identity` as an initContainer and `efbundle` as its container, so they apply in sequence
rather than racing. It is named in [`DEPLOYMENT.md`](DEPLOYMENT.md), which is where the ordering
requirement lives too — the migration finishes, successfully, before any pod that expects the new
schema starts taking traffic.

`SECURITY.md` states what a deployment owes here beyond the mechanics.

## When `dotnet ef` refuses to run

Failing on `Settings file 'DotnetToolSettings.xml' was not found in the package`, the local tool
manifest is what is broken, not the package. A globally installed copy invoked by its full path
works:

```bash
dotnet tool install --global dotnet-ef
$HOME/.dotnet/tools/dotnet-ef migrations add <Name> --project …
```

## Removing a feature

Removing an example feature touches the business migration and nothing else — authentication's is a
different file, in a different project, with a history table of its own, so no removal can reach
it. The business half ships a single initial migration, so on a project that has not yet applied one
to a real database the correct move is to regenerate rather than to write a `DropTable`.
[`REMOVING-THE-EXAMPLE-FEATURES.md`](REMOVING-THE-EXAMPLE-FEATURES.md#the-migrations) is the
procedure, including what changes once a database *has* applied one.
