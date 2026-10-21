# Migrations

## When

The model changed: a feature, a column, an index. Each context keeps its own history, and each
project holds exactly one context, so naming the project names the context.

| Context | Project | History table |
|---|---|---|
| `AppDbContext`, the business schemas and `platform` | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence` | `public.__EFMigrationsHistory` |
<!--#if (localAuth)-->
| `AuthDbContext`, the `identity` schema | `Src/Infrastructure/AppTemplate.Infrastructure.Auth` | `identity.__EFMigrationsHistory` |
<!--#endif-->

[The persistence model](../architecture/persistence-model.md) says why the data is split this way.

## The tool

`dotnet-ef` is a local tool, pinned in `.config/dotnet-tools.json` and restored by bootstrap. The
design-time factories read `ConnectionStrings__Default` from the environment and fall back to the
Compose database, so adding or checking a migration needs no running host.

## Add a migration

```bash
dotnet run Tools/Tasks.cs migration-add <Name>
```

The task targets the business context.
<!--#if (localAuth)-->
For authentication, name its project:

```bash
dotnet ef migrations add <Name> \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Auth \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Auth \
  --output-dir Migrations
```
<!--#endif-->

Each context ships one `InitialCreate`. While no database has applied it, change it rather than
stacking migrations: delete the two `InitialCreate` files and the model snapshot under the project's
`Migrations` folder, then run `migration-add InitialCreate`. Once a database has applied it, add a
named migration instead.

An empty migration means a configuration nobody applies: a new entity needs its schema constant, its
set and its `ApplyConfiguration` call in the context.

## Check it against the model

<!--#if (localAuth)-->
```bash
dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Auth \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Auth
```
<!--#endif-->
<!--#if (!localAuth)-->
```bash
dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
```
<!--#endif-->

It compares the snapshot with the model and needs no database. `verify` runs it for every context,
and `PendingModelChangesTests` proves the same inside the suite, which is how CI catches drift.

## Apply them

In Development, the API applies pending migrations at start.
<!--#if (localAuth)-->
It applies authentication's first, because the seed writes to its tables.
<!--#endif-->
Every other environment starts without touching the schema; a release applies them as its own step,
as [deployment](deployment.md#migrations-at-release) describes.

```bash
dotnet run Tools/Tasks.cs database-update    # the business context, against ConnectionStrings__Default
```

## Seeding

<!--#if (localAuth)-->
The only seed is a development administrator, off by default. Set `IdentitySeed:Enabled` to `true`
and `IdentitySeed:AdminPassword` in user secrets; the seeder refuses to run outside Development, and
starting without a password fails. [Accounts](../features/accounts.md#development-seed) has the details.
<!--#endif-->
<!--#if (!localAuth)-->
Nothing is seeded. Data a feature needs at start belongs in its migration.
<!--#endif-->

## Your own database

Supply the connection string through user secrets; `AppTemplate.Api.csproj` already carries a
`UserSecretsId`:

```bash
cd Src/Presentation/AppTemplate.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=…;Database=…;Username=…;Password=…"
```

Never put a secret in `appsettings.json` or `appsettings.Development.json`: both are tracked. A
missing or out-of-range setting fails the host at start, naming the key.
[The configuration reference](../reference/configuration.md#connectionstrings) lists the `Database`
settings beside it.

## When dotnet ef refuses

A failure on `Settings file 'DotnetToolSettings.xml' was not found in the package` is the local tool
cache, not the package. Install the tool globally and call it by its path:

```bash
dotnet tool install --global dotnet-ef
$HOME/.dotnet/tools/dotnet-ef migrations add <Name> --project … --startup-project …
```
