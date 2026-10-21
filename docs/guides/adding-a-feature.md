# Adding a feature

## When

The product needs a capability of its own: its records, its rules, its routes. Decide its shape first,
with the one question [feature shapes](../architecture/feature-shapes.md) asks: does the feature have
rules that protect a state?

<!--#if (publicApi)-->
This project was generated with `--auth none`, and `new-feature` refuses it: both shapes it writes
need an authenticated caller. Add an authentication scheme and an `ICurrentUser` that reads it first,
or write a public feature by hand from the steps below.

<!--#endif-->
## Generate it

```bash
dotnet run Tools/Tasks.cs new-feature <Features> <Feature> --access owner|shared \
    [--deletion delete|anonymise] [--operations get,list,create,update,delete] \
    [--model record|aggregate] [--concurrency none|etag]
```

`<Features>` is the folder, plural (`Orders`); `<Feature>` the type, singular (`Order`).

| Option | Default | Meaning |
|---|---|---|
| `--access` | required | `owner`: every operation is scoped to the caller, and another caller's record answers like a missing one. `shared`: every authenticated caller reaches the same records |
| `--deletion` | required with `owner` | the fate of the rows when their owner's account is deleted: `delete` or `anonymise`. Refused with `shared` |
| `--operations` | all five | any subset of `get`, `list`, `create`, `update`, `delete`; `create` needs `get` |
| `--model` | `record` | `record`: a service over a store, no domain model. `aggregate`: a domain aggregate and one use case per operation |
| `--concurrency` | `none` | `none`: last write wins. `etag`: a persisted version, `ETag` on reads, `If-Match` on writes. `aggregate` needs `etag` |

Every refused combination is refused before anything is written, with the change that fixes it.

The command writes files and edits none. It writes:

- the feature's folders in each layer, and tests that hold for any feature of its shape;
- `.feature-manifest/<Features>.json`: the options and the SHA-256 of every file written;
- the feature page `docs/features/<features>.md`, from `docs/_page-templates/feature.md`.

Then it prints the edits that compose the feature: the schema, set and configuration in
`AppDbContext`, an `Add<Features>()` in `ApplicationModule`, the adapters in `PersistenceModule`, the
call in `Program.cs`, and the two test compositions. The solution compiles again after the first edit,
and the feature composes after the last. An owner feature that is the project's first prints five more,
for the account-deletion sweep it brings with it.

## What you write by hand

The generator writes the shape; the rules are yours. Each step below has a test that reads it, so run
`dotnet test --project Tests/Architecture/AppTemplate.Architecture.Tests` before you push.

### Domain

For `aggregate` only: the invariants, in the factory and in the rehydration path alike, the value
objects, and the domain events the feature raises. [Domain and results](../architecture/domain-and-results.md)
says how a rule fails.

### Application

The validator's rules, the errors the feature can answer, and any use case beyond the five
operations, which [`new-use-case`](#add-a-use-case) starts. A use case reaches another feature only
through that feature's ports and events.

### Persistence

The record's columns, indexes and constraints in its EF configuration. The store or the repository
stays one capability; [ports and adapters](../architecture/ports-and-adapters.md) names the shapes.

### API

The request and response contracts, and anything beyond default-deny authorisation. The
[HTTP API](../architecture/http-api.md) conventions apply to every route.

### Tests

The rules the feature protects, each with a test you have seen fail, and the integration tests against
PostgreSQL under `Tests/Integration/AppTemplate.Api.IntegrationTests`;
[testing](testing.md#where-a-new-features-tests-go) says which type holds what. Add the feature's
schema to the schemas `TestDatabase` truncates between tests.

### Migration

<!--#if (localAuth)-->
The business context needs a migration; authentication's is untouched.
<!--#endif-->
<!--#if (!localAuth)-->
The business context needs a migration.
<!--#endif-->

```bash
dotnet run Tools/Tasks.cs migration-add Add<Features>
```

On a project no database has applied a migration to, regenerate `InitialCreate` instead, as
[migrations](migrations.md#add-a-migration) shows.

## Add a use case

A feature of the rich shape grows one operation at a time:

```bash
dotnet run Tools/Tasks.cs new-use-case <Features> <UseCase> --kind command|query [--input id|none]
```

`<UseCase>` names the operation, a verb and its subject: `Approve<Feature>`, `Get<Feature>Totals`.

| Option | Default | Meaning |
|---|---|---|
| `--kind` | required | `command`: changes state and answers a `Result`, under `UseCases/Commands/`. `query`: reads and answers a `Result` of a DTO named for it, under `UseCases/Queries/` |
| `--input` | `id` | `id`: a command or query carrying the id of the aggregate the feature's repository names, with its validator and its tests. `none`: no input, no validator, no test |

The command writes in the application layer and edits nothing:

- the use case's folder: its command or query, its validator, its named interface and the use case;
- for a query, its DTO under `Model/Dtos/`, unless the feature already declares one of that name;
- with `--input id`, one unit test that an empty id is refused before the use case acts, with its
  control.

Nothing is registered by hand: `AddFeature` in `ApplicationModule` finds the use case and its
validator by folder. When the feature has a manifest, the files join it, so `remove-feature --apply`
removes them with the feature while they are untouched.

The use case answers its validation, then throws `NotImplementedException` until its body is written:
the rule in the aggregate, the load, the call and the commit, each decision with its test. The command
prints the action, the using and the constructor parameter that serve it over HTTP, and for a query
the response contract; then rewrite the OpenAPI reference with `dotnet run Tools/Tasks.cs openapi`.

It refuses, writing nothing, a feature that does not exist, a feature of the simple shape, whose
operations are methods of its service, a use case the feature already has, and `--input id` where the
feature's repositories do not name one aggregate.

## The closed lists a feature joins

Some lists change only on purpose, and a test fails until they name what you added:

| You added | What has to say so |
|---|---|
<!--#if (anyFeature)-->
| A registration | `HostComposition` in the architecture tests and the call in `ApplicationModuleTests` |
<!--#endif-->
<!--#if (!anyFeature)-->
| A registration | `HostComposition` in the architecture tests |
<!--#endif-->
| A route or a contract | the OpenAPI reference, rewritten by `dotnet run Tools/Tasks.cs openapi` and read in the review |
| An error code | its row and status in [the API reference](../reference/api.md#error-codes), checked both ways by `ErrorCodeTableTests` and against the status by `ErrorCodeStatusTests` |
| An `IEntityTypeConfiguration` | `AppDbContext`, checked both ways by `PersistenceModelTests` |
| A new folder word under `Common/` or `Features/<F>/` | the vocabularies in `LayoutConventionTests` |
| A domain event | a consumer, or its name and reason in `DomainEventTests._deliberatelyUnconsumed` |
| A public interface under `Common/` that is not a port | `ApplicationPorts.NotPorts`, with its reason |
| An anonymous endpoint | `_anonymousActions` in `HttpSurfaceTests` |
| A background service | the floor in `BackgroundWorkTests`; an instrument in `ObservabilityRegistrationTests` |
| A new project | `AppTemplate.sln`, a `COPY` line in the API's `Dockerfile`, a mirror under `Tests/`, the floors in `ModuleDependencyTests` |
| Anything, eventually | `coverage.minimum`, re-measured as [testing](testing.md#coverage) says |

## Document it

1. Replace each prompt line of the feature page with what the feature does: what a caller gets, the
   rules, the routes, the settings, the tests.
2. Add the row the generator printed under `## Features` in `docs/README.md`.
3. A choice another team could have made differently is an ADR: `docs/decisions/`, numbered from 1000,
   linked from the page's `## Decisions`.

<!--#if (anyFeature)-->
## Patterns from the examples

| When the feature | Read |
|---|---|
<!--#endif-->
<!--#if (todoLists)-->
| owns child entities and protects a state | TodoLists, in every layer |
| is a flat aggregate reached from a timer | Reminders, and its job under the API's `Features/Reminders/Jobs` |
<!--#endif-->
<!--#if (files)-->
| keeps part of its state outside the database | Files, whose bytes live behind a storage port |
<!--#endif-->
<!--#if (notes)-->
| has no state to protect | Notes, a service over a store: the shape `--model record` writes |
<!--#endif-->

## Promote a simple feature

A record feature that gains a rule moves to the rich shape; the routes, contracts, `ETag` and owner
scoping do not change:

1. Extract the aggregate, its factory and rehydration, and its repository port.
2. Replace the store by the repository: keep the record, add the mapper and the tracker; the queries
   port stays.
3. Split the service into one use case per operation.
4. Point the controller at the use cases.

The migration is empty unless the rule needs a column or an index.

<!--#if (anyWeb)-->
## Web pages for it

Run `pnpm api` in `Src/Web` once the API serves the routes: the generated client then holds their
types, and an app calls them through it. [The web front end](../features/web-frontend.md#calling-the-api)
shows the call.
<!--#endif-->

## Undo

`dotnet run Tools/Tasks.cs remove-feature <Features>` reports what removing it involves;
[removing features](removing-features.md#remove-a-generated-feature) says when `--apply` deletes it.
