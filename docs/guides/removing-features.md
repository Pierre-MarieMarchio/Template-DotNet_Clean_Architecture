# Removing features

## When

When a part has served its purpose, this page removes:

- a feature you generated with `new-feature`
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
dotnet run Tools/Tasks.cs remove-feature <Features>           # reports, writes nothing
dotnet run Tools/Tasks.cs remove-feature <Features> --apply   # deletes an untouched skeleton
```

The report reads `.feature-manifest/<Features>.json`: the files the feature owns, the lines that
register or use it elsewhere, and the checks to run after. `--apply` deletes the files, the manifest,
the emptied folders and the feature page, but refuses, deleting nothing, once a file was edited or
added or a registration exists; remove such a feature by hand from the report. The first owner feature
holds the account-deletion files later ones share, so it goes last.

Then remove its row from `docs/README.md`, and change the migration as [below](#migrations).

<!--#if (todoLists || notes)-->
## Remove the examples

<!--#endif-->
<!--#if (todoLists)-->

Reminders depends on TodoLists: it consumes `TodoItemCompletedDomainEvent` and reads
`ITodoItemQueries`, so removing TodoLists removes Reminders too. Notes stands alone.
<!--#endif-->
<!--#if (todoLists || notes)-->

### What to delete

Delete `Features/<Feature>/` in every layer that has one, and its mirror under `Tests/`:
<!--#endif-->
<!--#if (todoLists)-->

- TodoLists and Reminders in the domain, application, persistence and API; the API folders carry the
  jobs.
- `Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/` and
  `Src/Infrastructure/AppTemplate.Infrastructure.InMemory/Features/Reminders/` with Reminders.
- `Tests/Application/AppTemplate.Application.UnitTests/TestDoubles/ATodoList.cs` and `AReminder.cs`,
  and the integration folders `TodoLists/` and `Reminders/`.
<!--#endif-->
<!--#if (notes)-->
- Notes in the application, persistence and API, and
  `Tests/Presentation/AppTemplate.Api.UnitTests/Composition/NotesSavingTests.cs`.
<!--#endif-->
<!--#if (todoLists && !files)-->
- `Common/Tagging/` in the domain and the application, with its tests: nothing is left tagged.
<!--#endif-->
<!--#if (todoLists || notes)-->

Delete a parent folder left empty; `LayoutConventionTests` names one you missed.

### What to edit

| File | Change |
|---|---|
| `Src/Application/AppTemplate.Application/ApplicationModule.cs` | the feature's `Add<Features>()` method and its constant |
| `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/PersistenceModule.cs` | the `Add<Features>Feature` method and its call |
| `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs` | the set, the schema constant, the `ApplyConfiguration` calls |
| `Src/Presentation/AppTemplate.Api/Program.cs` | the `Add<Features>()` line |
<!--#if (presentationBlazorServer)-->
| the presentation project's `Program.cs` | the same line |
<!--#endif-->
<!--#if (jobsWorker)-->
| `Src/Presentation/<Project>.Worker/Program.cs` | the same line |
<!--#endif-->
| `Tests/Architecture/AppTemplate.Architecture.Tests/Composition/HostComposition.cs` | the same calls |
| `Tests/Application/AppTemplate.Application.UnitTests/ApplicationModuleTests.cs` | the feature's call and the facts naming its types |
| `Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/TestDatabase.cs` | the feature's schema |
<!--#endif-->
<!--#if (todoLists)-->
<!--#if (!jobsWorker)-->
| `Src/Presentation/AppTemplate.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs` | `AddReminderJobs` |
| `Src/Presentation/AppTemplate.Api/Features/BackgroundJobs/Jobs/BackgroundJobsTelemetry.cs` | the reminder source and meters |
<!--#endif-->
<!--#if (jobsWorker)-->
| `Src/Presentation/<Project>.Worker/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs` | `AddReminderJobs` |
| `Src/Presentation/<Project>.Worker/Features/BackgroundJobs/Jobs/BackgroundJobsTelemetry.cs` | the reminder source and meters |
<!--#endif-->
| `Src/Infrastructure/AppTemplate.Infrastructure.Email/EmailModule.cs` | the reminder notifier; `IEmailSender` stays |
| `Src/Infrastructure/AppTemplate.Infrastructure.InMemory/InMemoryModule.cs` | `AddInMemoryReminderNotifications` |
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
<!--#if (todoLists || notes)-->
- the feature's requests in `AppTemplate.Api.http`;
- the feature's rows in [the configuration](../reference/configuration.md) and [API](../reference/api.md)
  references, and in [adding a feature](adding-a-feature.md#patterns-from-the-examples).

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
`Storage/`, and the projects `Src/Infrastructure/AppTemplate.Infrastructure.Storage` and
`Tests/Infrastructure/AppTemplate.Infrastructure.Storage.UnitTests` with their solution entries. Then
edit:

- the four registration files above, and `AddStorageModule` in `Program.cs`;
- the storage reference in `AppTemplate.Api.csproj` and its `COPY` line in the API's `Dockerfile`;
- `AddFileJobs` and the file instruments of the background jobs;
- the `Files` and `Jobs:Files` settings, and the object store in Compose and `.env.example`;
- [the Files page](../features/files.md) and [ADR 0018](../decisions/0018-stored-files-are-keyed-granted-and-refused.md),
  with their index rows.

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

<!--#if (localAuth)-->
## Remove authentication

Delete `Src/Application/AppTemplate.Application.Auth`, `Src/Infrastructure/AppTemplate.Infrastructure.Auth`
and `Src/Presentation/AppTemplate.Api/Features/Auth` with their tests and solution entries, and the
`AddAuthFeature` line in `Program.cs`; the `identity` migration goes with its project. The host then
owes an authentication scheme of its own and the `Administrator` policy.
<!--#endif-->
<!--#if (localAuth && reminders)-->

Reminders reads its recipient from the local accounts: replace `AccountReminderRecipientDirectory`
with a directory over your provider, or remove Reminders too.
<!--#endif-->
<!--#if (localAuth)-->

`TheApiContainer_BuildsWithoutAuthentication` proves the rest composes without it.

<!--#endif-->
## Verify

```bash
dotnet run Tools/Tasks.cs verify
docker build -f Src/Presentation/AppTemplate.Api/Dockerfile .
```

The build catches the unused usings, the tests the counts, lists and migration, and the image build
a stale `COPY` line.
