---
type: guide
title: Removing features
description: How to remove a feature, an example or another part this project no longer needs.
---

# Removing features

## When

When a part has served its purpose, this page removes:

- a feature you generated with `add feature`
<!--#if (todoLists || notes)-->
- the examples, once you have read them
<!--#endif-->
<!--#if (files)-->
- Files, when the product stores no file
<!--#endif-->
<!--#if (localAuth)-->
- the local accounts, when an identity provider replaces them
<!--#endif-->

Removing is three things in every case: delete the feature's folders, delete its registrations, and
change the business migration.

## Remove a generated feature

```bash
dotnet cleanarch remove feature <Features>           # reports, writes nothing
dotnet cleanarch remove feature <Features> --apply   # removes it, edited or not
```

The report reads `.feature-manifest/<Features>.json` and sorts what removing the feature involves:

- what `--apply` removes: the files the generators wrote and nobody changed since, the files the
  fields `add field` added changed among them, the feature page, and each edit the manifest records,
  those `add use-case`, `add setting` and `add event` made among them, each undone exactly as it was
  made;
- what it removes though it changed since: a file of the feature edited, or one added to its folders;
- what is already gone, which it leaves as it is;
- what is left to you, with its file and line: what still names the feature outside it and no
  manifest records. Code that uses its types, its namespace or its registration stops `--apply`,
  since the build breaks once the feature is gone.
<!--#if (api)-->
  The keys of its settings in the hosts' settings files, `.env.example` and Compose, their
  sections in [the configuration reference](../reference/configuration.md), the links to its page,
  and its routes and error codes in [the API reference](../reference/api.md) and the `.http` samples
  break nothing and go stale: the report lists them, and `--apply` prints them again once done.
<!--#endif-->
<!--#if (!api)-->
  The keys of its settings in the hosts' settings files, `.env.example` and Compose, their
  sections in [the configuration reference](../reference/configuration.md) and the links to its page
  break nothing and go stale: the report lists them, and `--apply` prints them again once done.
<!--#endif-->

A line is listed when it names the feature where a name means something: an identifier in code, with
its strings and comments set aside, a key in a settings file, a heading of the configuration
reference, a link to the feature's page, its route or its error code. The feature's name in a
sentence refers to nothing, so the report leaves it out.
<!--#if (api)-->
It reads neither the solution, the `.props` and `.targets` files nor `tools/`, nor what is written
again from the code: the generated clients, the web workspace's API schema, the OpenAPI reference and
the migrations.
<!--#endif-->
<!--#if (!api)-->
It reads neither the solution, the `.props` and `.targets` files nor `tools/`, nor the migrations.
<!--#endif-->

`--apply` changes nothing while a file it would delete or edit is not committed as it stands, since
only `git restore` gives back what it removes, while a model change waits for a migration
(`migrations add --check`), or while code outside the feature uses it.
<!--#if (api)-->
It then deletes and undoes everything or nothing, writes the documentation index again without the
feature's page, builds once, and from that build adds the `Remove<Features>` migration, as
[below](#migrations) says, and rewrites the OpenAPI reference and what is written from it.
<!--#endif-->
<!--#if (anyWeb)-->
With a web workspace, that rewrite includes `pnpm api <reference>` in `src/Web`, which needs Node.js and pnpm.
<!--#endif-->
<!--#if (!api)-->
It then deletes and undoes everything or nothing, writes the documentation index again without the
feature's page, builds once, and from that build adds the `Remove<Features>` migration, as
[below](#migrations) says.
<!--#endif-->
An edit is found by its lines, beside the line it was placed beside or, when another feature's lines
came between, wherever its lines stand once; an edit whose lines changed since stops `--apply`, naming
the file, until they are put back or removed by hand and committed. The first owner feature holds the
account-deletion files later ones share, so it goes last, and a feature another one uses goes after it.
<!--#if (todoLists && notes)-->

A folder word of a layer that no feature is filed under any more fails
`EveryFeatureFolder_IsNamedFromItsLayersVocabulary` in `LayoutConventionTests`: drop the word from that
layer's vocabulary there, and from the folder map of [the project layout](../reference/project-layout.md).
<!--#endif-->
<!--#if (todoLists || notes)-->

## Remove the examples
<!--#endif-->
<!--#if (todoLists)-->

Reminders depends on TodoLists: it consumes `TodoItemCompletedDomainEvent` and reads
`ITodoItemQueries`, so removing TodoLists removes Reminders too. Notes stands alone.
<!--#endif-->
<!--#if (todoLists || notes)-->

### What to delete

Delete `Features/<Feature>/` in every layer that has one, and its mirror under `tests/`:
<!--#endif-->
<!--#if (todoLists)-->

- TodoLists and Reminders in the domain, application, persistence and API; the API folders carry the
  jobs.
- `src/Infrastructure/AT.Infrastructure.Email/Features/` and
  `src/Infrastructure/AT.Infrastructure.InMemory/Features/Reminders/` with Reminders.
- `tests/Application/AT.Application.UnitTests/TestDoubles/ATodoList.cs` and `AReminder.cs`,
  and the integration folders `TodoLists/` and `Reminders/`.
- [the TodoLists page](../features/todo-lists.md) and [the Reminders page](../features/reminders.md).
<!--#endif-->
<!--#if (notes && api)-->
- Notes in the application, persistence and API, and
  `tests/Presentation/AT.Api.UnitTests/Composition/NotesSavingTests.cs`.
- [the Notes page](../features/notes.md).
<!--#endif-->
<!--#if (notes && !api)-->
- Notes in the application, persistence and each host.
- [the Notes page](../features/notes.md).
<!--#endif-->
<!--#if (todoLists && !files)-->
- `Common/Tagging/` in the domain and the application, with its tests: nothing is left tagged.
<!--#endif-->
<!--#if (todoLists || notes)-->

Delete a parent folder left empty; `LayoutConventionTests` names one you missed.

### What to edit

| File | Change |
|---|---|
| `src/Application/AT.Application/ApplicationModule.cs` | the feature's `Add<Features>()` method and its constant |
| `src/Infrastructure/AT.Infrastructure.Persistence/PersistenceModule.cs` | the `Add<Features>Feature` method and its call |
| `src/Infrastructure/AT.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs` | the set, the schema constant, the `ApplyConfiguration` calls |
| <!--#if (presentationBlazorServer)--> |
| the portal's `Program.cs` | the `Add<Features>()` line |
| <!--#endif--> |
| <!--#if (presentationMcp)--> |
| the MCP server's `Program.cs` | the `Add<Features>()` line |
| the MCP server's `Program.cs`, `Features/<Features>/Tools` and its tests, and `TestSupport/ToolCatalogue.cs` | the feature's `WithTools` line, its tools, their tests and their names |
| <!--#endif--> |
| <!--#if (api)--> |
| `src/Presentation/AT.Api/Program.cs` | the `Add<Features>()` line |
| <!--#endif--> |
| <!--#if (jobsWorker)--> |
| `src/Presentation/<Initials>.Worker/Program.cs` | the same line |
| <!--#endif--> |
| `tests/Architecture/AT.Architecture.Tests/Composition/HostComposition.cs` | the same calls |
| `tests/Application/AT.Application.UnitTests/ApplicationModuleTests.cs` | the feature's call and the facts naming its types |
| <!--#if (api)--> |
| `tests/Integration/AT.Api.IntegrationTests/Infrastructure/TestDatabase.cs` | the feature's schema |
| <!--#endif--> |
| <!--#endif--> |
| <!--#if (todoLists)--> |
| <!--#if (!jobsWorker)--> |
| `src/Presentation/AT.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs` | `AddReminderJobs` |
| `src/Presentation/AT.Api/Features/BackgroundJobs/Jobs/BackgroundJobsTelemetry.cs` | the reminder source and meters |
| <!--#endif--> |
| <!--#if (jobsWorker)--> |
| `src/Presentation/<Initials>.Worker/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs` | `AddReminderJobs` |
| `src/Presentation/<Initials>.Worker/Features/BackgroundJobs/Jobs/BackgroundJobsTelemetry.cs` | the reminder source and meters |
| <!--#endif--> |
| `src/Infrastructure/AT.Infrastructure.Email/EmailModule.cs` | the reminder notifier; `IEmailSender` stays |
| `src/Infrastructure/AT.Infrastructure.InMemory/InMemoryModule.cs` | `AddInMemoryReminderNotifications` |
<!--#endif-->
<!--#if (todoLists || notes)-->

Then build: each unused `using` is an error the build names. Lower the floors the architecture tests
report, and drop a removed event from `DomainEventTests._deliberatelyUnconsumed`.

### Configuration and samples

None of these breaks a build, and each goes stale:
<!--#endif-->
<!--#if (todoLists)-->

- the `Jobs:Reminders` section of both `appsettings` files, and the `JOBS_REMINDERS_*` lines of
  `.env.example` and `docker-compose.yml`;
<!--#endif-->
<!--#if ((todoLists || notes) && api)-->
- the feature's requests in `AppTemplate.Api.http`;
- the feature's rows in [the configuration](../reference/configuration.md) and [API](../reference/api.md)
  references and in [adding a feature](adding-a-feature.md#patterns-from-the-examples); then run
  `dotnet cleanarch docs-index`, which drops the feature's page from the index.
<!--#endif-->
<!--#if ((todoLists || notes) && !api)-->
- the feature's rows in [the configuration reference](../reference/configuration.md) and in
  [adding a feature](adding-a-feature.md#patterns-from-the-examples); then run
  `dotnet cleanarch docs-index`, which drops the feature's page from the index.
<!--#endif-->
<!--#if (todoLists || notes)-->

### What stops being demonstrated
<!--#endif-->
<!--#if (todoLists)-->

With TodoLists and Reminders go one event reaching two features' consumers, the `304` answer to a
conditional `GET`, and the tests whose subject is a to-do list.
<!--#endif-->
<!--#if (notes)-->

With Notes goes the only example of the simple shape.
<!--#endif-->
<!--#if (files)-->

## Remove Files

Files is a working capability: a product that stores files re-points it at its own bucket. To remove
it, delete `Features/Files/` in every layer with its test mirrors, the integration folders `Files/` and
`Storage/`, and the projects `src/Infrastructure/AT.Infrastructure.Storage` and
`tests/Infrastructure/AT.Infrastructure.Storage.UnitTests` with their solution entries. Then
edit:

- the four registration files above, and `AddStorageModule` in `Program.cs`;
<!--#if (api)-->
- the storage reference in `AppTemplate.Api.csproj` and its `COPY` line in the API's `Dockerfile`;
<!--#endif-->
- `AddFileJobs` and the file instruments of the background jobs;
- the `Files` and `Jobs:Files` settings, and the object store in Compose and `.env.example`;
- [the Files page](../features/files.md), then `docs-index`, which drops it from the index.
<!--#endif-->
<!--#if (todoLists && files)-->

`Common/Tagging/` goes when neither TodoLists nor Files is left.
<!--#endif-->

## Migrations

<!--#if (localAuth)-->
Removing a business feature changes the business migration only; authentication has its own.
<!--#endif-->
While no database has applied the initial migration, recreate it as
[migrations](migrations.md#the-initial-migration) shows. Once a database has applied it, add a
migration that drops the tables, and review it line by line.
Where no database has applied `Add<Features>` or `Remove<Features>`, neither is needed: delete the
context's `Migrations` folder and run `bootstrap` again, as
[migrations](migrations.md#the-initial-migration) shows, and the first migration holds the model
without the feature.

`remove feature --apply` leaves `Add<Features>`, the migration `add feature` added, and each
`Add<Field>To<Features>` that `add field` added, as they are, since a database may have applied them,
and adds `Remove<Features>` after them: it drops the feature's tables, and its schema when
`Add<Features>` created one, and its `Down` puts them back. A database that applied the others
applies it next; one that applied none applies them all and ends without the feature.

<!--#if (localAuth && api)-->
## Remove authentication

Delete `src/Application/AT.Application.Auth`, `src/Infrastructure/AT.Infrastructure.Auth`
and `src/Presentation/AT.Api/Features/Auth` with their tests and solution entries, and the
`AddAuthFeature` line in `Program.cs`; the `identity` migration goes with its project. The host then
owes an authentication scheme of its own and the `Administrator` policy.
<!--#endif-->
<!--#if (localAuth && !api)-->
## Remove authentication

Delete `src/Application/AT.Application.Auth`, `src/Infrastructure/AT.Infrastructure.Auth` and the
`Features/Auth` folder of each host under `src/Presentation` with their tests and solution entries, and
the line that composes it in each host's `Program.cs`; the `identity` migration goes with its
project.
<!--#endif-->
<!--#if (localAuth && reminders)-->

Reminders reads its recipient from the local accounts: replace `AccountReminderRecipientDirectory`
with a directory over your provider, or remove Reminders too.
<!--#endif-->
<!--#if (localAuth)-->

`TheApiContainer_BuildsWithoutAuthentication` proves the rest composes without it.

<!--#endif-->
## Verify

<!--#if (api)-->
```bash
dotnet cleanarch verify
docker build -f src/Presentation/AT.Api/Dockerfile .
```
<!--#endif-->
<!--#if (!api)-->
```bash
dotnet cleanarch verify
docker build -f src/Presentation/<Initials>.Worker/Dockerfile .
```
<!--#endif-->

The build catches the unused usings, the tests the counts, lists and migration, and the image build
a stale `COPY` line.
