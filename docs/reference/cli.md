---
type: reference
title: Project tool
description: What dotnet cleanarch offers this project, command by command, and what still runs through Tools/Tasks.cs.
---

# Project tool

`dotnet cleanarch` is this project's own .NET tool, pinned in `.config/dotnet-tools.json` at the
version of the template the project came from and restored with the other local tools. This page
lists what each of its commands does, the options it takes, the tasks of `Tools/Tasks.cs` it stands
for and when it is refused, then what still runs through `Tools/Tasks.cs`.

## Running it

The tool finds this project by `cleanarch.json`, in the working directory or the nearest folder
above it, and runs every command from the folder that holds it. Outside a project it says so and
fails, and a `cleanarch.json` it cannot read is named with the reason.

| Command | Does |
|---|---|
| `dotnet cleanarch` | names this project, the template package and version it came from, and its root |
| `dotnet cleanarch --help`, `dotnet cleanarch help` | lists the commands this project has, built-in and its own |
| `dotnet cleanarch help --json` | the same as JSON: each command with its options, and for one of this project's own its `local` file, with `replacesBuiltIn` true where it replaces a built-in command |
| `dotnet cleanarch --version` | the tool's version, which is the template's |

## The rules every command keeps

- A command that runs an external tool prints the exact line it runs, runs it at the project root
  and forwards its exit code, so the printed line gives the same result without the tool.
- A command takes the name the tool its users know gives it: `add` and `remove` as `dotnet add`
  has them, `migrations add` and `database update` as `dotnet ef` has them.
- A command that keeps a file in step takes `--check`, which fails where it would write and writes
  nothing: `format`, `migrations add` and `openapi`.
- A command that creates files takes `--dry-run`, which prints what it would write and writes and
  runs nothing: each `add` command, `eject` and `adopt`. A command that deletes reports by default and
  writes only with `--apply`: `remove feature`.
- `help` lists only what `cleanarch.json` makes true. A command that does not apply to this project
  is hidden from it, and running it prints the reason and fails.

## Building and testing

| Command | Does | Stands for |
|---|---|---|
| `build` | `dotnet build` of the solution | `build` |
| `test` | `dotnet test` of the solution; `--no-integration` builds every test project and runs only the ones that need no Docker daemon, naming each one it built and did not run | `test` |
| `coverage` | the tests with coverage under `TestResults`, then `dotnet run Tools/CoverageGate.cs` against the floor `coverage.minimum` states | `coverage` |
| `verify` | the whole gate, below | `verify` |
| `format` | `dotnet format` of the solution, every Markdown file saved as UTF-8 without a BOM with LF endings, then each name that breaks a naming rule (IDE1006), which `dotnet format` cannot rename; it fails while one is left | `format-fix` |
| `format --check` | `dotnet format --verify-no-changes` over the style rules the build does not enforce (IDE0003, IDE0049), then each Markdown file `format` would save; it writes nothing | `format` |

`build`, `test`, `coverage` and `verify` take `--configuration` (`-c`), Debug by default, and
`-m:<N>`, which caps the build's nodes and the test modules run at once on a machine short of
memory; with it, `test` builds first, then runs the tests it built. Off Windows they leave out a test
project that targets Windows alone, and say so.

`verify` refuses to start while a database context has no initial migration. It then runs, stopping
at the first step that fails: the self-tests of the scripts under `Tools/`, the documentation index
check `dotnet run Tools/Tasks.cs docs-index --check`, the comparison of the .NET client with what
Kiota writes from the OpenAPI reference where the project holds the client, the web workspace's
schema check, `pnpm audit`, `pnpm install --frozen-lockfile`, `pnpm lint`, `pnpm test` and
`pnpm build` where `cleanarch.json` records a web workspace, then the restore, `format --check`, the
build, every test, the pending-model-changes check of each context and the vulnerable-packages
listing.
<!--#if (templateMaintenance)-->
In this repository it also runs the template's own gates, the web lockfiles' check and the
launcher's solution.
<!--#endif-->

## The database

<!--#if (localAuth)-->
Each command targets the business context, or the identity context with `--context auth`.
<!--#endif-->
<!--#if (!localAuth)-->
Each command targets the business context, the only one this project holds; `--context auth` is
refused, since only a project with local accounts holds an identity context.
<!--#endif-->

| Command | Does | Stands for |
|---|---|---|
| `migrations add <Name>` | `dotnet ef migrations add`; `--no-build` reads the model from the context's last build. A migration with nothing in it, since the model has not changed, is deleted with what it wrote, the model snapshot restored, and the command fails | `migration-add` |
| `migrations add --check` | `dotnet ef migrations has-pending-model-changes`: fails while the model holds a change no migration holds; it takes no name | `migration-check` |
| `migrations bundle` | one self-contained bundle per context under `artifacts/migrate`; takes `--configuration` | `migration-bundle` |
| `database update` | `dotnet ef database update` | `database-update` |

Each of them is refused, running nothing, while a context it targets has no initial migration yet,
and names `bootstrap`, which creates it.

## Bootstrap

`bootstrap` sets this project up once, right after generation.
<!--#if (api)-->
It runs, in this order and stopping at the first step that fails: `presentation-scaffold`,
`dotnet format` of the usings and the whitespace (IDE0005 and IDE0055), `docs-index`, the OpenAPI
reference as `openapi` writes it, `dotnet tool restore`, the initial migration of each context
whose `Migrations` folder holds none, the .NET client as `openapi` writes it where the project
holds the client, then `web-scaffold` where `cleanarch.json` records a web app.
<!--#endif-->
<!--#if (!api)-->
It runs, in this order and stopping at the first step that fails: `presentation-scaffold`,
`dotnet format` of the usings and the whitespace (IDE0005 and IDE0055), `docs-index`,
`dotnet tool restore`, the initial migration of each context whose `Migrations` folder holds none,
then `web-scaffold` where `cleanarch.json` records a web app.
<!--#endif-->
A context that has its migrations
keeps them as they are. When a step fails, it names that step, says what is done and what is not,
and forwards the step's exit code; what is done stays done, and `bootstrap` runs again once the
cause is fixed. On success it says what it did, to commit before anything else.

It does not install itself: the tool that runs it is the one this project pins. Where the tool is
not pinned yet, `dotnet run Tools/Tasks.cs bootstrap` pins it first, as
[the first run](../getting-started/first-run.md) says.

<!--#if (api)-->
## The API

| Command | Does | Stands for |
|---|---|---|
| `openapi` | builds the API and writes the OpenAPI reference it describes itself with, then everything written from it: the .NET client's `Generated/` folder through Kiota where the project holds the client, and the web workspace's schema with `pnpm api --no-build` where the workspace holds the `api-client` package | `openapi`, `api-client`, and `pnpm api` in the web workspace |
| `openapi --no-build` | the same from the API's last build, refused while it has none | `openapi --no-build` |
| `openapi --check` | fails where the reference, the client or the web schema differs from what it would write, and writes nothing | `api-client --check` |
| `run` | `dotnet run` of the API | `run` |
| `end-to-end` | the Compose stack from `.env.example` under a project name of its own, the end-to-end tests against it, then with a web app the browser smoke, the stack's logs on a failure, and the stack and its volumes removed; `--keep-stack` leaves it running, `--configuration` sets the tests' | `end-to-end` |

<!--#endif-->
## The stack

| Command | Does | Stands for |
|---|---|---|
| `compose up` | `docker compose up -d --wait` | `compose-up` |
| `compose down` | `docker compose down` | `compose-down` |
| <!--#if (sonar)--> |
| `sonar` | starts the SonarQube service of the Compose file, then scans from its scanner | `sonar` |
| <!--#endif--> |

## Adding and removing

| Command | Does | Stands for |
|---|---|---|
| <!--#if (canAddFeature)--> |
| `add feature <Features> <Feature>` | a feature in every layer, its manifest under `.feature-manifest/` and its page, composed; `--access owner` with `--deletion delete` or `anonymise`, or `--access shared`; `--operations`, `--model record` or `aggregate`, `--concurrency none` or `etag`, `--fields` | `new-feature` |
| `add field <Features> "<fields>"` | fields added to a feature `add feature --fields` wrote, each that cannot be null with the default the stored rows receive | `new-field` |
| <!--#endif--> |
| `add use-case <Features> <UseCase>` | one operation on a feature of the rich shape; `--kind command` or `query`, `--input` | `new-use-case` |
| `add setting <Features> <Section>` | a feature's options class and validator, registered, with its section in each host's `appsettings.json` and the configuration reference; `--keys` | `new-setting` |
| `add event <Features> <Event>` | a domain event of a feature of the rich shape, with `--consumer <Features>:<Reaction>` or `--unconsumed "<reason>"`, and `--fields` | `new-event` |
| `remove feature <Features>` | reports what removing the feature involves and writes nothing; with `--apply`, refused while what it changes is not committed, removes it as its manifest records it | `remove-feature` |
<!--#if (!canAddFeature)-->

`add feature` and `add field` are refused: this project authenticates nobody, and every feature they
write needs an authenticated caller.
<!--#endif-->

Each writes every file and edit or none, and `--dry-run` prints them and writes and runs nothing.
[Adding a feature](../guides/adding-a-feature.md) and [removing features](../guides/removing-features.md)
give the steps around them.

| Command | Does | Stands for |
|---|---|---|
| `add web-app` | installs the template package at the version `cleanarch.json` records, runs `dotnet new cleanarch-webapp`, then its join step | `dotnet new cleanarch-webapp`, then `web-join` |
| `add web-package` | the same with `cleanarch-webpackage`; it exists once `cleanarch.json` records a web workspace | `dotnet new cleanarch-webpackage`, then `web-join` |
| `add presentation` | the same with `cleanarch-presentation`, then its join and scaffold steps | `dotnet new cleanarch-presentation`, then `presentation-join` and `presentation-scaffold` |

They ask the item template's questions and answer the ones about this project from `cleanarch.json`.
Each takes `--name` (`-n`), `--yes` (`-y`) for the default of every option not given,
`--template-source` for a checkout of the template or a `.nupkg`, `--hive` for the template engine's
settings folder, `--dry-run`, and one `--<option>` per option of its template. Run where the item
template's files are already present, they complete the rest.

## This project's own commands

A file-based app `Tools/<name>.cs` runs as `dotnet cleanarch <name>`, through
`dotnet run Tools/<name>.cs -- <arguments>`, which the tool prints and runs from the working
directory; the app receives every argument after its name and the tool forwards its exit code. A new
name adds a command, and a built-in name replaces the built-in command, even one `cleanarch.json`
refuses; `help` says which is which.

Only a file named as a command is one: lower-case words joined by hyphens, as `end-to-end` is.
`Tools/Tasks.cs` and every other file named in PascalCase stay scripts run with `dotnet run`, and a
file named `help` is no command: `help` stays the tool's, since it says which commands are this
project's own.

## Ejecting a command

`dotnet cleanarch eject <command>` copies a built-in command's source into `Tools/<command>.cs`, a
file-based app this project then owns and runs in place of the built-in one; that command stops
following the tool's releases. `--dry-run` says what it would write and writes nothing.

`help`, `eject`, `adopt`, `add` and `remove` stay the tool's: `help` says which commands are this
project's own, `eject` makes them, `adopt` runs before the tool knows the project, and `add` and
`remove` run on libraries a file-based app does not carry. A command `cleanarch.json` refuses has
nothing to eject, and a `Tools/<command>.cs` already there is never overwritten.

`dotnet cleanarch adopt` writes `cleanarch.json` for a project generated before the tool, from its
files, and is refused where one exists; `--dry-run` prints it, and `--template-version` gives the
version where `docs/getting-started/overview.md` no longer names it.

## What still runs through Tools/Tasks.cs

These tasks have no command of the tool, and `dotnet run Tools/Tasks.cs bootstrap` stays the one
that pins it:

| Task | Runs |
|---|---|
| `check` | what `verify` would catch on the files a change touches |
| `restore` | `dotnet restore` of the solution, which `verify` runs as a step |
| `docs-index` | the index of each folder under `docs/`, and its `--check` |
| `hygiene` | the self-tests of the gates, then `docs-index --check` |
| `upgrade-report` | what a newer version of the template changes in this project |
| `presentation-scaffold`, `web-scaffold` | each presentation project or web app still holding its scaffold file |

The tool also runs some of its steps through `Tools/Tasks.cs`, and prints each one:

- `bootstrap` runs `presentation-scaffold`, `docs-index` and `web-scaffold`.
- `add web-app` and `add web-package` run `web-join`, and `add presentation` runs
  `presentation-join`, then `presentation-scaffold`.
<!--#if (canAddFeature && api)-->
- `add feature` and `remove feature --apply` run `migration-check` before they write, then
  `docs-index`, `build`, `migration-add` from that build, and with the API `openapi`, `api-client`
  where the project holds the .NET client and `pnpm api --no-build` where the web workspace holds
  the `api-client` package. `add field` runs `build` and `migration-add`, then the same contract
  steps, and `add use-case` the contract steps alone.
<!--#endif-->
<!--#if (canAddFeature && !api)-->
- `add feature` and `remove feature --apply` run `migration-check` before they write, then
  `docs-index`, `build` and `migration-add` from that build; `add field` runs `build` and
  `migration-add`.
<!--#endif-->
<!--#if (!canAddFeature && api)-->
- `remove feature --apply` runs `migration-check` before it writes, then `docs-index`, `build`,
  `migration-add` from that build, and with the API `openapi`, `api-client` where the project holds
  the .NET client and `pnpm api --no-build` where the web workspace holds the `api-client` package;
  `add use-case` runs those contract steps alone.
<!--#endif-->
<!--#if (!canAddFeature && !api)-->
- `remove feature --apply` runs `migration-check` before it writes, then `docs-index`, `build` and
  `migration-add` from that build.
<!--#endif-->
- `verify` runs `docs-index --check`, and `coverage` runs `Tools/CoverageGate.cs`.
