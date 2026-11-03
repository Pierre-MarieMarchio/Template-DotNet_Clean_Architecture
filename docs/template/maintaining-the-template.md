# Maintaining the template

This repository builds, tests and runs as the default generation does: every condition is a comment
the compiler ignores. A change is done when the project's gate passes here and every generated shape
still holds.

## The template gates

`dotnet run Tools/Tasks.cs verify` runs the project's gate plus what only the template carries:

| Gate | Checks |
|---|---|
| `Tools/CheckDocPaths.cs` | every repository path in a Markdown code span exists, and every `docs/` path a workflow, script or `.http` file names; `CHANGELOG.md` and the plans folder are exempt |
| `Tools/CheckWorkflows.cs` | what a YAML parse misses: a dangling `needs:`, an action on a mutable tag, an image without a digest, a missing `permissions:`, an undeclared `$VAR`, a path absent from disk, a trigger filtered by `paths:` or `paths-ignore:`, a job-level `if:` on a pull request's matrix job |
| `Tools/CheckComments.cs` | no comment outside the allowlist, one guidance line at a time in configuration files, no Markdown narrating the repository's history |
| `web-lockfiles --check` | each generated web shape's lockfile equals what the pinned pnpm writes for it; needs pnpm (`corepack enable`) |
| `api-client --check` | `Src/Clients/AT.Client/Generated` equals what the pinned Kiota writes from the OpenAPI reference; needs the tools `dotnet tool restore` restores |

Each gate runs its `--self-test` first, against fixtures that must make it fire and fixtures that
must leave it silent, and fails when it examined nothing. A rule added to a gate comes with the fixture
that fails without it. These three files are excluded from generation, and the project's `hygiene`
task asks the filesystem which gates its tree carries, so one task serves both.

CI runs them in `.github/workflows/ci.yml`, under `templateMaintenance` blocks a generated project
does not receive. Its end-to-end job skips its one step in this repository, whose stack includes web
apps no CLI has created yet; the acceptance matrix runs the suite instead.

### The published package each week

`.github/workflows/template-published-package.yml` runs every Monday at 05:41 UTC, off the top of the
hour, and on demand. It installs the latest stable `CleanArch.WebApi.Templates` nuget.org serves,
in a scratch hive and without a checkout, generates the default project, bootstraps it, which restores
the project's local tools, `CleanArch.Cli` among them once the template pins it, from nuget.org, and builds it with warnings as errors, so NuGetAudit
judges the dependencies users receive today. Nothing else builds the published package between
releases: the acceptance matrix packs the checkout. A red run is an advisory published since the last
release, or a published package that no longer builds; the fix is a release that raises the
dependency, as [the project's policy](../decisions/0030-any-known-advisory-fails-the-build.md) says.
A nuget.org holding no stable version fails the run too, since there is nothing for a user to install.
Like the nightly acceptance run, GitHub announces a red run only to the person who last changed the
cron line, by mail if their settings send workflow failures, and shows it under the Actions tab; look
there on Mondays. The workflow is excluded from generation and from the package, and
`rules.json` routes it to no leg.

## Migrations in this checkout

The template ships no migration, so this checkout has none until
`dotnet run Tools/Tasks.cs migration-initial` creates them, as bootstrap does in a project; the
tests, `verify` and the drift check need them. They are git-ignored here only, by a `templateMaintenance` block of `.gitignore`, and
both `template.json` and `.template.config/TemplatePackage.csproj` exclude them, so a project
generated from this checkout or from its package starts without them; `TemplatePackagingTests` holds
the three. After a model change, delete each context's `Migrations` folder and run the task again. The
CI and release workflows run it before building, under `templateMaintenance` blocks.

## The acceptance matrix

`.github/workflows/template-acceptance.yml` packs the template package and `CleanArch.Cli` once, chooses the legs the
change can affect, and per leg installs that `.nupkg`, generates under another name, records the using directives it wrote, asserts the source name leaked nowhere, checks
its `cleanarch.json` against the leg's options with `.template.config/acceptance/CheckProjectFile.cs`, bootstraps
(the project's tool pinned from that packed `CleanArch.Cli`, which `dotnet cleanarch` then names
the project with, the formatting, which fails the leg if it removes one of those usings, as
[usings under a marker](#usings-under-a-marker) says, then `migration-initial` after asserting no migration was generated, and once more to
prove it creates nothing), builds with warnings as errors, runs `format` for what the build does not check, runs the Docker-free tests, checks every generated context for
pending model changes, runs the generated project's `hygiene`, and checks every path its pages cite
with this checkout's `Tools/CheckDocPaths.cs`. The legs that set `integration` also run the
integration suites, on the runner's Docker through Testcontainers, and the defaults, SQL Server and
SQLite defaults legs then run the generated project's `dotnet run Tools/Tasks.cs end-to-end` against
its Compose stack. The leg that sets `coverage`, the worker, MCP and Blazor WebAssembly leg with
external sign-in and every example, then runs the generated project's `coverage` task, every suite
again under coverage, against the `coverage.minimum` it carries, which every shape shares and nothing
lowers to pass; its suites take under a minute, so the step adds about two minutes to a leg of about
seven and a half. The defaults leg, the local-auth leg without examples and the external-auth leg
without examples also run the generator round trip: `new-feature` writes an owner feature, a shared one, an aggregate one and an
aggregate one on `get,list,create` in turn, the first and the third with `--fields` naming a field of
every type, nullable ones and bounds among them, the aggregates after `new-use-case` adds a command, a
query, a command without input, a query with typed inputs and a command with the id and a body to
each while refusing the others. Each command makes its own edits, the account-deletion wiring of a
project's first owner feature among them, adds the feature's migration and rewrites the OpenAPI
reference and the client; the leg fails when a manifest records no edit or no migration was added.
The whole solution then builds with warnings as errors, the architecture tests pass, the feature's own
tests pass in every Docker-free test project, and the business context has no pending model change.
Every file no manifest lists then goes back to the commit, the edits, the migration and the
rewritten references among them, untracked files included, since `remove-feature` refuses a feature
still composed; `remove-feature --apply` then leaves `git status` empty. Then all four are written
together, proved the same way, and on the defaults leg, which has Docker, the integration tests
`--fields` wrote run against PostgreSQL, about half a minute more; `hygiene` passes, and they are
removed last in first out. A web leg also checks its
lockfile, adds a second app and a package with the item templates, scaffolds, runs the workspace's
install, lint, test and build, fails when an app with a build script has no built page, checks a
second `web-scaffold` changes nothing, starts the whole stack and asks each app for its page, its
headers and an answer only the API gives; it fails when a served page holds an event-handler
attribute or an inline script the served `script-src` allows neither by its hash nor by the
script's nonce, or when two responses share a nonce, or when a server-rendered app with accounts
relays a `POST` without `X-CSRF` instead of refusing it itself, and reports its inline styles; a page that
loads the WebAssembly authentication script must carry the origin of `EXTERNAL_AUTH_AUTHORITY` in
its served `connect-src` and `frame-src`. The every-starter leg adds
each framework starter its first app is not, so one workspace proves them all together. Every app,
package and presentation project a leg adds with an item template is recorded by the project's
`web-join` or `presentation-join` task, and a web leg runs `ProjectFileTests` once more after its
last addition. Every leg
runs `presentation-scaffold` before the formatting, a step that does nothing without a presentation
project, and a web leg creates the WebAssembly app of `--web blazor-wasm` there too, so the build
compiles it and its tests run with the others. After the build, every leg checks that bootstrap left
whitespace and encoding unchanged; the `defaults` leg alone runs the `format` task, whose rules need a
compilation, as `ci.yml` runs it on the template. The three web legs that pass `--presentation
blazor-server`, one per authentication mode, create the presentation project, build and test it, and
ask it in the stack for its page or its redirect to the sign-in page, for the sign-in form under local accounts,
and for `/health`. The leg that adds a first app adds the presentation project too, with
`cleanarch-presentation`, makes the edits its `README.md` lists with the generated project's
`presentation-join` task, and from there runs as those three do. The legs that pass `--presentation worker`
build and test the worker with the rest; the generator leg among them writes the deleted-account sweep
into the worker, and the two that start the stack fail unless the worker, still running, logs a
completed pass within two minutes of the stack being up, and the API starts no job loop. Each of
those checks greps the logs captured whole, since `grep -q` closing a pipe early fails the pipeline
under `pipefail` and reads a logged pass as none. The legs that pass `--presentation mcp` with the API start the stack and
call the MCP server over raw JSON-RPC: it answers `/health` and refuses a call without a credential; under
an external provider its `401` names its resource metadata, which names the provider; under local
accounts the leg signs the seeded administrator in, creates an access key that writes and one that
only reads through the API, creates a note and reads it back through the tools, is refused a write
with the read-only key, revokes the key and is refused with it. The several-hosts leg starts the API,
the Blazor portal, the MCP server and the worker in one stack, and the leg without the API generates,
builds and tests a portal, an MCP server and a worker with no HTTP API at all.

| Leg | Options |
|---|---|
| defaults | none |
| several hosts, local auth, notes | `--presentation blazor-server --presentation mcp --presentation worker --examples notes --files false`, the API brought by the MCP server's access keys |
| local auth, no files, no ci, no sonar | `--auth local --examples none --files false --ci false --sonar false --presentation api --presentation worker` |
| worker, mcp and blazor wasm, external auth, all examples | `--presentation api --presentation mcp --presentation worker --auth external --examples all --files true --web blazor-wasm` |
| sql server, avalonia, external auth, all examples | `--database sqlserver --auth external --examples all --files true --presentation avalonia` |
| no api, blazor server, mcp and worker, external auth, no agent files | `--presentation blazor-server --presentation mcp --presentation worker --auth external --examples notes --files false --agent-files false` |
| external auth, no examples, no files | `--auth external --examples none --files false`, then `cleanarch-presentation --framework avalonia` |
| sqlite, external auth, all examples | `--database sqlite --auth external --examples all --files true`, then `cleanarch-presentation --framework mcp` |
| sqlite, defaults | `--database sqlite`, then `cleanarch-presentation --framework wpf` |
| web placeholder, sqlite, smallest project | `--web placeholder --database sqlite --auth none --examples none --files false --presentation blazor-server`, then `cleanarch-webapp --framework angular --rendering ssr` |
| web react back office, local auth | `--web vite-react --web-audience admin --presentation blazor-server`, then `cleanarch-webapp --framework blazor-wasm` and `web-join` before the build |
| web angular, external auth, notes | `--web angular --web-rendering ssr --auth external --examples notes --presentation blazor-server`, then `cleanarch-webapp --framework blazor-wasm --auth external` and `web-join` before the build |
| no web, then a react app, its workspace and an angular back office | none, then `cleanarch-webapp --framework vite-react --workspace`, `cleanarch-presentation --framework avalonia`, `cleanarch-webapp --framework blazor-wasm --has-client true` and `cleanarch-webapp --framework angular --audience admin --rendering ssr` |
| web, every starter | `--web blazor-wasm --examples none --files false`, then `cleanarch-presentation --framework avalonia --has-client true` and `cleanarch-webapp` once per pnpm framework starter, each on its own `--port`, and once more for React Router with `--rendering ssr` |
| windows, wpf, local auth | `--presentation wpf`, every other option at its default, on `windows-latest` |
| windows, wpf, external auth, notes | `--presentation wpf --auth external --examples notes`, on `windows-latest` |

A leg is one generated shape that carries as many guarantees as its options allow: every value of
every option, every item template and every check the workflow runs only for some legs is proved by
at least one leg, and two shapes share a leg unless one turns off a part the other must prove. An
item template adds a presentation project only to a project generated without one, since it writes
`docs/features/presentation.md`.
A leg stays under about twelve minutes, since the slowest one sets the run's length, and the account
runs at most twenty jobs at once, which the acceptance legs share with the other workflows.

Every leg also greps its project for the other database providers' packages and types, reading its own
`database`; the SQLite legs run their integration suites on a file and start no database container, and
the SQL Server leg starts one large image, so it has the longest timeout. A leg without a web app asserts no workflow, Dependabot entry, Compose file or ignore file names a
workspace. The leg that adds a first app runs that assertion, then `cleanarch-webapp --workspace` and
`web-join`, which makes the lines `Src/Web/README.md` and the app's `README.md` list, and is a web leg
from there. `--workspace` only adds files, and
the engine refuses to overwrite one, so everything a web app needs from the API ships with every
project and the web jobs are `web.yml`, not jobs of `ci.yml`. A separate job asserts each refused combination exits non-zero and writes no file. The legs
are the shapes the options produce, not their cross product, and run with `fail-fast: false`.

A leg whose `runner` is `windows-latest` runs in the `template-windows` job instead, as a Windows
user runs the project ([template decision 0014](decisions/0014-a-windows-only-shape-is-proved-on-a-windows-runner.md)):
from PowerShell, one command a step, it installs the package, generates, checks its `cleanarch.json`,
asserts the source name leaked nowhere, then runs `bootstrap`, the `build` task with warnings as errors,
`test --no-integration`, `hygiene` and the citation check. A Windows runner runs no Linux image, so
the job starts no container and sets up no web workspace. It checks out the root files, `Tools` and
`.template.config/acceptance` with the files beside it alone, since the package brings the template and a starter's deepest path exceeds the 260 characters
git for Windows allows by default; their `.gitattributes` keeps them LF whatever the runner's
`core.autocrlf`.

The legs are the entries of `.template.config/acceptance/matrix.json`: the fields the workflow's
steps read, and the `tags` that name what the leg generates. The two web legs with external
authentication add to their stack's `.env` the provider variables the API needs to start; no
provider runs in Compose, and the API fetches nothing from the one it names until a token arrives.

### Which legs a change runs

A pull request runs the legs its change can affect, and every other leg still reports
([template decision 0010](decisions/0010-a-pull-request-runs-the-legs-its-change-can-affect.md)).
The workflow's `Select the acceptance legs` job runs `.template.config/acceptance/SelectLegs.cs`
after its `--self-test`, on the paths `git diff --name-only --no-renames HEAD^1 HEAD` lists for the
pull request's merge commit. Each rule of `.template.config/acceptance/rules.json` maps path globs to
tags, to `none` (no leg reads the path) or to `all`; the first rule a path matches decides, a path no
rule matches runs every leg, and a leg runs when it carries a tag a changed path names. The
`template` job takes the Linux legs to run as its matrix and `template-windows` the Windows ones;
the `skipped` job reports each other leg green, whichever its runner, with one echo step, under the
same name, `Template acceptance (<leg>)`, the check the `develop` ruleset requires.

| A change to | Runs |
|---|---|
| `docs/template`, the plans, `Launcher`, `README.md`, `CHANGELOG.md`, the template-only gates and tests, the release, pull request title and web lockfiles workflows, the release configuration | no leg |
| `Src/Web`, `.github/workflows/web.yml`, the `cleanarch-webapp` and `cleanarch-webpackage` item templates, the placeholder's lockfile | the six web legs |
| a framework starter under `.template.config/starters` | the legs that generate it: the every-starter leg alone for Vite and React Router |
| `.template.config/starters/blazor-server`, `.template.config/starters/avalonia`, `.template.config/starters/wpf`, `.template.config/starters/mcp`, the `cleanarch-presentation` item template | the legs that pass that `--presentation`, or add the project with the item template |
| `.template.config/starters/worker` | the legs that pass `--presentation worker` |
| a `.template.config/variants/database-<name>` overlay | that provider's legs |
| anything else: `Src`, `Tests`, the shared pages, `Tools`, the props, `template.json`, the other variants | every leg, the Windows ones included |

The `windows, wpf, local auth` leg generates the default shape with the WPF app added and nothing
else changed, so it proves the default project's tasks on Windows too. The two WPF legs carry the
WPF starter's tag, as the Linux leg that adds the app with `cleanarch-presentation` does: a change to
the starter builds the app and runs its `.Core` tests on Linux, and builds, tests and checks it on
Windows under both kinds of sign-in.

Every leg runs on a push to `main`, on a manual run, on the nightly run of `develop`, under the
`full-acceptance` label, on an empty or unreadable diff, and on a change to the selector, its rules,
the matrix or the workflow. A push to `develop` starts no acceptance run: its pull request ran the
legs its change could affect, and the nightly run proves the merged tree whole.

The nightly run starts at 02:17 UTC, off the top of the hour when GitHub delays scheduled runs, and
runs only when `develop` has moved: the `Has develop moved since the last scheduled run` step
compares the commit with the one of the last scheduled run that finished green or red, a cancelled
one not counting, and passes `--moved false` to the selector when they are the same; a list it
cannot read counts as a move. A red night is announced by GitHub alone: it emails the person who
last changed the cron line, if their
notification settings send workflow failures, and shows the run under the Actions tab of
`develop`; look there after a merge when no mail came. GitHub disables a scheduled workflow after 60
days without repository activity, which a `develop` that moves rarely can reach.

Labelling a pull request starts a run, so `full-acceptance` applies without a push. Any other label
starts a run that chooses no leg and reports none, in a concurrency group of its own, so it cancels
no run in progress; only `Pack the template package` and `Template refuses unsupported options` run
again, on the same tree. The jobs it skips report under the name `Template acceptance
(${{ matrix.name }})`, which no ruleset requires, so no required leg turns green unproved.

`AcceptanceSelectionTests`, a template-only rule, keeps the two files honest: the legs are the
checks the ruleset requires plus the every-starter leg, which it does not; every folder under
`.template.config/starters` and `.template.config/variants`, and each item template's, has a rule of
its own; every tag a rule names is a leg's, and every leg's tag a rule's; each leg's tags are the ones
its options choose; each leg's runner is one a job serves, a Windows leg sets only the fields
`template-windows` reads and generates no web app, and one Windows leg passes `--presentation wpf`
and nothing else; and every `none` path is left out of the template
package. `Tools/CheckWorkflows.cs`
refuses a `paths:` filter, which leaves a required check pending, and a job-level `if:` on a pull
request's matrix job, which reports none of its legs when it skips them all, unless its matrix is read
from an earlier job and another job of the same name reports the legs it leaves out.

### Adding a leg

A new guarantee first looks for a leg whose shape it fits: options that turn off nothing the leg
proves, and a leg that stays under about twelve minutes. It gets a leg of its own only when none does.

1. Add its entry to `.template.config/acceptance/matrix.json`, with the fields its steps read and
   the tags its options choose, and `"runner": "windows-latest"` for a shape a Windows user must
   see run; `AcceptanceSelectionTests` fails until they agree.
2. Add the leg's name to the required list in `AcceptanceSelectionTests`, and once that pull request
   merges, `Template acceptance (<leg>)` to the required checks of the `develop` ruleset, which
   would otherwise hold every other pull request pending; a leg left optional, as the every-starter
   leg is, goes to that test's other list.
3. Give a folder the leg is the first to generate a rule in `.template.config/acceptance/rules.json`,
   and the leg its row in the table of legs above.

## Database providers

A project carries one provider ([decision 0005](decisions/0005-a-project-carries-one-database-provider.md)),
and none ships a migration: `bootstrap` creates each context's `InitialCreate` from the project's own
model. PostgreSQL is in-tree: compiled, tested and covered in this checkout.
Every other provider is a whole-file overlay under `.template.config/variants/database-<name>/`: each
file replaces the PostgreSQL file at the same path, with the same type names and public members, and
`template.json` excludes the PostgreSQL file under `(!postgreSql)`. The rest of the code, the domain
and the application never name a provider.

| Overlaid file | Supplies |
|---|---|
| `Src/Infrastructure/AT.Infrastructure.Core/Common/Contexts/DatabaseProvider.cs` | `DesignTimeConnectionString`, `VersionColumnName`, both `UseDatabase` overloads (design time, and run time with `DatabaseOptions` and `ContextMigrations`), `ApplyDatabaseConventions`, `IsVersion`, `IncludingColumns`, `WhereColumnEquals`, `IsUniqueViolation`, `DedicatedConnectionString` |
| `Src/Infrastructure/AT.Infrastructure.Persistence/Common/Leases/LeaderLease.cs` | `LeaderLease`, the `ILeaderLease` adapter, constructed from `IConfiguration` and a logger |
| `Src/Infrastructure/AT.Infrastructure.Persistence/Features/Files/Adapters/Locks/OwnerQuotaLock.cs` | `OwnerQuotaLock`, constructed from the connection string and a logger, throwing `OwnerQuotaLockTimeoutException` after `WaitLimit` |
| `Src/Presentation/AT.Api/Common/Observability/DatabaseInstrumentation.cs` | the provider's tracing and metrics, or none |
| `docker-compose.database.yml` | the `db` service and the `database-client` service whose environment gives the api `ConnectionStrings__Default`; SQLite declares only `database-client`, which mounts the `db-data` volume |
| `Tests/Integration/AT.Api.IntegrationTests/Infrastructure/DatabaseServer.cs`, `Tests/Integration/AT.Infrastructure.Auth.IntegrationTests/Fixtures/DatabaseServer.cs` | the test server: `Image` (one `docker-compose.database.yml` runs), `ConnectionString`, `StartAsync`, `DisposeAsync`, and `Table(schema, table)`, the name a test's raw SQL uses; the API one adds `TablesSql`, `ResetSql`, `SchemasSql`, `HistorySchemasSql(table)`, and `Value` and `Literal` for a `Guid` or a `DateTimeOffset` as the provider stores it; the Auth one adds `TransientFault` |
| `Tests/Infrastructure/AT.Infrastructure.Core.UnitTests/Common/Contexts/DatabaseProviderTests.cs`, `Tests/Infrastructure/AT.Infrastructure.Auth.UnitTests/Fixtures/DatabaseFaults.cs`, `Tests/Infrastructure/AT.Infrastructure.Persistence.UnitTests/Features/Files/Adapters/Locks/OwnerQuotaLockTests.cs`, `Tests/Infrastructure/AT.Infrastructure.Persistence.UnitTests/PersistenceModuleConnectionStringTests.cs` | the provider's own unit tests and faults; an overlay may leave one out by a `template.json` exclusion |

A provider without `xmin` registers `VersionStampingSaveChangesInterceptor` from its run-time
`UseDatabase`, which the modules call after the saving interceptors; `IsVersion` makes `Version` a
plain concurrency token. A provider without schemas adds `SchemaAsTablePrefixConvention` in
`ApplyDatabaseConventions` and names each history table with its `TableName`. Both are in-tree and
tested on SQLite here, and shipped only under `versionCounter` and `tablePrefixes`.

The shared files carry every provider's lines already: the package references of
`Src/Infrastructure/AT.Infrastructure.Core/AppTemplate.Infrastructure.Core.csproj` and of the
two integration projects, the versions in `Directory.Packages.props`, the variables of `.env.example`,
and the `developmentConnectionString` switch that rewrites `appsettings.Development.json`. An overlay's
compose file uses the variable names `.env.example` gives it.

A provider is refused until its overlay is wired. Wiring one is three edits to `template.json`: a
source copying the folder under its computed symbol, the removal of its `NotYetGenerated` refusal with
its `dotnetcli.host.json` entry, and its removal from `refusedOptions`. `TemplatePackagingTests`
fails until the three agree with the folder. Its refusal line in the acceptance refusal job becomes a
leg, tagged `database-<name>` with the overlay's rule in `.template.config/acceptance/rules.json`, and
each leg's provider-leak check reads the leg's `database`.

This version generates PostgreSQL, SQL Server and SQLite. MySQL stays refused, because the stable EF
Core 10 providers cannot map the owned JSON entity Identity's passkeys use; no page names a MySQL
branch, and the `tablePrefixes` symbol covers SQLite alone until it is generated.

An overlay file starts as a copy of its PostgreSQL counterpart, so `git log` on both paths finds a
change one of them missed.

## Adding an option

1. Declare the parameter in `.template.config/template.json` with its `displayName`, and a computed
   symbol when pages and code need a name for a combination. A hidden option or a long name goes in
   `dotnetcli.host.json`. A visible option gets its label in `ide.host.json`, which Visual Studio
   reads and which hides every symbol it does not list; `TemplatePackagingTests` refuses a refusal
   shown or an option missing there. Its place there is the order the launcher asks in: what the
   project holds, then how callers sign in, then its data, then its tooling, so that an answer
   leaves out what it rules out in the questions after it.
2. Exclude whole paths with a source modifier under a condition; condition content inside a file in
   that file's own comment syntax.
3. A combination that cannot hold together is a hidden required parameter, and the same condition in
   `refusedOptions`, so the `#error` variant catches a hand-passed bypass.
4. Add the leg that generates the new shape, as [adding a leg](#adding-a-leg) says, and a refusal
   to the refusal job.
5. Condition every page whose truth changes, and judge each in every form it can take.
6. An option that is not a part joins `cleanarch.json`, as [the project file](#the-project-file)
   says.

## Writing docs that condition

Project pages reach every generated project, so each is written for each shape. Inside Markdown:

```markdown
<!--#if (files)-->
The object store keeps the bytes.
<!--#endif-->
<!--#if (!files)-->
No object store runs.
<!--#endif-->
```

- Use only real symbols of `template.json`, negated with `!` for the other branch; never a bare
  else, elseif or a marker inline in a sentence.
- A branch holds whole sentences, whole list items or whole table rows; a table's header stays outside
  any branch that can leave rows without it.
- No marker inside a mermaid fence. A diagram shows the full shape, unconditioned, and says so.
- A page about an absent part is excluded by a path condition, not emptied by markers; its links from
  shared pages are conditioned the same way.
- `AGENTS.md` is the only agent file, and `--agent-files false` leaves it out: a page that points to
  it conditions that sentence or row on `agentFiles`, and points to the page that owns the rule
  otherwise. Keep `AGENTS.md` concise and pragmatic, and link to a page rather than restate it.
- A link from a shared file (`CONTRIBUTING.md`, `AGENTS.md`, `SECURITY.md`) to `docs/template/` sits
  in a `templateMaintenance` block, which is always false at generation.
- A shared file conditions a line on a part a project can gain later (a web app, a presentation host,
  the worker) only when the item template that adds the part rewrites the file or its join edits it;
  otherwise the project that gains the part reads otherwise than one generated with it, and
  `Launcher/CleanArch.Create.Tests/EndToEndTests.cs` fails. The issue forms and the pull request
  template under `.github` stay true in every form instead.
- Text that names a token (`AppTemplate`, `app-template`, the web app tokens) is replaced in a
  generated project; write a token only where its replacement reads right.
- Project pages say "this project"; only `docs/template/` and the repository `README.md` say "the
  template".

Read a page in the default form, in `--auth none --examples none --files false`, and in each form its
markers distinguish: no broken link or anchor, no marker left, no orphan punctuation, no page about an
absent module.

## Usings under a marker

A using stands under the condition of the lines that read what it brings in, no wider: a using kept
in a form where nothing reads it is IDE0005, which fails that form's build, warnings being errors.
The legs build some forms only, and bootstrap's formatting would remove such a using before their
build sees it, so two checks hold the rule.

| Check | What it judges | Where |
|---|---|---|
| `Tests/Architecture/AT.Architecture.Tests/Rules/ConditionedUsingTests.cs` | every form `template.json` can generate, a leg's or not: each combination of options it does not refuse, the files its sources and modifiers write for it, and the lines of each C# file its markers keep, `//#if` and `/*#if ... #endif*/` alike; a using of a project namespace no kept line reads fails, naming the file, the namespace and the options of a form that keeps it | template-only, in the architecture tests |
| `Record the using directives the template wrote`, then `Bootstrap formatting` | the forms the legs generate, every using alike: a using bootstrap's IDE0005 removes from a file the template wrote fails the leg | `.github/workflows/template-acceptance.yml` |

The test judges a namespace by the names the template's own C# files declare in it, types, their
attributes' short names and extension methods, and a line as reading a name it spells. It reads the
files under `.template.config/variants` and `.template.config/starters` as the forms that write them,
and the client Kiota writes in `Src/Clients/AT.Client/Generated` for the names it declares only. It
leaves out what it cannot know: a namespace from a package, and the projects an official CLI creates,
whose `scaffold.json` has the kind `dotnet`; the acceptance check covers those in the shapes the legs
build.
A form the test reports runs with the options it names: generate it and build it to see IDE0005.

The same holds for a tool's own methods: a method of a file under `Tools` stands under the condition
of its callers, no narrower, or a form keeps a call to a method its markers removed and
`dotnet run` cannot compile the tool there. No project builds a tool, so
`Tests/Architecture/AT.Architecture.Tests/Rules/ConditionedToolMethodTests.cs` judges it: in every
form `template.json` can generate, a call kept to a method the file declares, with no declaration
kept, fails, naming the tool, the method and the options of a form that keeps the call.

## Web sources

Two folders under `Src/Web/apps` are the source of both `--web` and `cleanarch-webapp`:
`PlaceholderSite`, the static page, and `FrameworkSite`, the wiring every framework app shares (its
`Dockerfile`, `nginx.conf`, `compose.yml` and `README.md`). The project template keeps the one the
choice needs and renames it from `--web-name`, the item template renames it from `-n`. Each name is a
token in three spellings (`PlaceholderSite`, `placeholder-site`, `PLACEHOLDER_SITE`, and the same for
`FrameworkSite`). Where one framework differs, the wiring says so by a marker on its own symbol:
Angular's inline styles in `nginx.conf`, its `NG_CLI_ANALYTICS` in the `Dockerfile`, React Router's
hash step and client folder in the `Dockerfile`, and the sentences of the `README.md` that name the
CLI.

The app itself comes from a starter, one folder per `--web` value under `.template.config/starters`
([template decision 0008](decisions/0008-a-web-app-comes-from-a-starter.md)): `angular`,
`vite-react`, `vite-vue`, `vite-svelte` and `react-router`, each holding
`FrameworkSite/scaffold.json` and `FrameworkSite/overlay/`, which both templates copy into the app's
folder by the framework chosen and `web-scaffold` consumes at bootstrap. Neither template copies anything else from there. A
framework has one value: React and Vue come from Vite's CLI, so `vite-react` and `vite-vue` hold
them, and no second value creates the same framework another way.

A starter whose generator also renders on the server holds that form in `ssr/FrameworkSite/`, its
own `scaffold.json` and `overlay/` ([template decision 0011](decisions/0011-a-starter-may-render-on-the-server.md)):
`angular` does, with `ng new --ssr`. Both templates copy the starter's folder without `ssr/`, leave
its `FrameworkSite/` out under `webServerRendering`, and copy `ssr/` under the starter's symbol and
`webServerRendering`; the shared `Dockerfile` then ends on Node, and `nginx.conf` is left out.
`--web-rendering ssr`, and `cleanarch-webapp --rendering ssr`, are refused for every other starter
by `serverRenderingNeedsAServerStarter`.

Adding a starter, for a CLI that keeps the contract of decision 0008:

1. Write `.template.config/starters/<value>/FrameworkSite/scaffold.json`: the CLI's package, its
   exact version, its arguments (`{name}` and `{directory}` are replaced; the CLI runs with its input
   closed, so every answer is an argument), the JSON merges that make it build into `dist/`, relay
   `/api/` and `/health`, name the package `framework-site` and give it the
   `@app-template/api-client` dependency and a `test` script; and `overlay/` for a file the CLI's own
   settings cannot reach, a Vite config being one. The workspace's scripts run once and rewrite
   nothing: a `test` that watches or a `lint` that fixes is merged into one that does not, and Vitest
   is pinned to the version `Src/Web/packages/api-client/package.json` uses. Import the client from
   a scratch file of the created app once and run its type-check: the workspace packages export
   their sources and import each other by `.ts` path, so a compiler setting the CLI leaves off, such
   as `allowImportingTsExtensions`, is a merge too. A file the starter does not need, such as the
   CLI's own `Dockerfile`, is named in `remove`. List the inline scripts of the built page: a page
   with none keeps the shared policy; a page with some needs the hash step of the `Dockerfile`, which
   React Router has, extended to the new symbol (and to its page's folder if the builder does not
   write it at the root of `dist/`). Never `'unsafe-inline'` for scripts, never a nonce.
2. Add `<value>` to the choices of `web` in `.template.config/template.json` and of `framework` in
   `.template.config/webapp/.template.config/template.json`, each description naming the generator
   and its template, with its computed symbol, a source copying the starter folder into
   `./Src/Web/apps/` under that symbol, and the symbol reached from `webFramework`: through
   `webVite` for a Vite-based starter, which also gives it port 5173 in `webDefaultPort` and
   `defaultPort`; directly otherwise, with its own port in both. `TemplatePackagingTests` fails until
   both templates agree with the folder. A framework that already has a value gets no second
   one.
3. Condition the wiring only where the new framework differs, and the README sentences that name
   its CLI and list what the starter adds. Give the value its row in
   `docs/getting-started/overview.md` and `docs/template/using-the-template.md`, and its name where
   `docs/features/web-frontend.md`, `Src/Web/README.md` and the repository `README.md` list the
   values.
4. Give it an accent colour in `Launcher/CleanArch.Tooling/Prompts/GuidedConversation.cs`; its
   question comes from the manifests.
5. To offer its server form, write `ssr/FrameworkSite/scaffold.json` and its `overlay/`, whose server
   relays through `relayToApi` of `@app-template/api-client/server`, sends the page headers, and
   renders with a nonce per response; add the folder's source and the two modifiers of its main
   source to both `template.json` files, and its value to the condition of
   `serverRenderingNeedsAServerStarter` and `refusedOptions`. `TemplatePackagingTests` fails until
   they agree with the folder, and a web leg generates the server form.
6. Add it to the every-starter leg of the acceptance matrix, with a port of its own, which
   scaffolds, builds and serves it beside the others and refuses an inline script its served policy
   does not allow by hash; a starter that differs in the wiring gets a leg of its own as well, unless
   the every-starter leg already proves what differs, as it does for React Router's hash step. Its
   folder's rule in `.template.config/acceptance/rules.json` names the tag `starter-<value>`, which
   each leg that generates it carries in `.template.config/acceptance/matrix.json`.

## Presentation starters

`--presentation` names every host a project holds, one or several, `api` by default
([template decision 0020](decisions/0020-a-project-is-the-presentation-projects-it-holds.md)).
Each value but `api` is a starter, of kind `dotnet`
([template decision 0009](decisions/0009-a-dotnet-project-comes-from-a-starter.md)) or, for WPF, the
MCP server and the worker, `written`, copied under its value's symbol
(`presentationBlazorServer`, `presentationMcp`, `presentationAvalonia`, `presentationWpf`,
`jobsWorker`, which `needsWorker` also sets). `needsApi` brings the API with what calls it, a web app, a desktop client or the MCP
server under local accounts, and `api`, the API chosen or brought, conditions every part of it: its
project and tests, the end-to-end suite, the load smoke, the `.http` file, its Compose service, the
tasks around the OpenAPI document and the stack, the architecture rules and pages about the HTTP
surface. `needsWorker` brings the worker to a project whose hosts hold neither the API nor the
worker, so that some host runs the jobs; `workerNeedsServerDatabase` refuses the worker, chosen or
brought, with SQLite, and `desktopIsOneFramework` Avalonia and WPF together.
`.template.config/starters/blazor-server` holds the project's own files at their paths in a project:
`.template.config/starters/blazor-server/Src/Presentation/AT.PresentationAppPortal` (its `scaffold.json`, project file,
`Program.cs`, settings, `Dockerfile` and wiring), and its test project beside them under `Tests/`; the
project template copies the folder into the project's root under `presentationBlazorServer`.
`.template.config/starters/avalonia` holds a desktop client's the same way, copied under
`presentationAvalonia`, which sets `dotnetClient`: it keeps the files `dotnet new avalonia.mvvm`
writes at its root and in `Assets`, `ViewModels` and `Views`, owns the project file, `Program.cs`,
`App.axaml.cs`, the main view model and window, and references the .NET client alone.
`.template.config/starters/mcp` holds an MCP server written whole
([template decision 0015](decisions/0015-an-mcp-server-is-a-presentation-project-written-whole.md)),
copied under `presentationMcp`: its `scaffold.json` is of kind `written`, in the shape of
`dotnet new mcpserver --transport remote`, so `presentation-scaffold` only adds it to the solution;
it owns the project file, `Program.cs`, the settings, the image, the caller's authentication (an
access key under `localAuth`, the provider's token under `externalAuth`) and one tools class per
example feature, each excluded with its feature. Under local accounts the access keys its callers
present come with it: `accessKeys` (`localAuth && presentationMcp`) keeps their files in the Auth
context and the API, their tests and ADR 0026, leaves them out otherwise, and conditions the lines
they add to shared files; `cleanarch-presentation` brings the same files and its `README.md` lists
those lines for `presentation-join`.
`.template.config/starters/wpf`, copied under `presentationWpf`, which sets `dotnetClient` too, is
written whole ([template decision 0013](decisions/0013-a-starter-the-generator-cannot-add-to-is-written-whole.md)):
the WPF app, on `net10.0-windows` with `EnableWindowsTargeting` so that it builds on every system,
holds `App.xaml`, `MainWindow.xaml` and their code-behind and references
`AppTemplate.PresentationAppDesktop.Core` alone; that project, on `net10.0`, holds the view models, the
settings, the host and the session, references the .NET client alone, and is the one its tests
reference, so they run on Linux too. Its session is a copy of the Avalonia starter's, in its own
namespace: `TheWpfStartersDesktopSession_IsTheAvaloniaStartersOwn_InTheCoresNamespace` fails until a
change to one copy is made to the other, and a third desktop client is the time to move them into a
project of their own.
Each starter carries a token of its own, so that two hosts never meet on a path, a service or a port:
`PresentationAppPortal` for Blazor, `PresentationAppMcp` for the MCP server, `PresentationAppDesktop`
for Avalonia and WPF, each with its kebab-case (`presentation-app-portal`) and upper-case
(`PRESENTATION_APP_PORTAL`) forms. The project template replaces them with the hosts' names, `Portal`,
`Mcp` and `Desktop`, by constant symbols, and `cleanarch-presentation` with `-n`. The portal answers
on 8090 and the MCP server on 8091; both read the settings every host shares from
`x-host-environment` in `docker-compose.yml`. A starter's C# is compiled only in a generated project,
so a change to it is proved by generating one: Blazor's by the acceptance legs that pass `--presentation blazor-server`,
Avalonia's by the leg that adds it with `cleanarch-presentation`, WPF's by the two Windows legs and
the SQLite leg that adds it, the MCP server's by the legs that pass `--presentation mcp` and
the SQLite external-auth leg that adds it with `cleanarch-presentation`.

`cleanarch-presentation`, in `.template.config/presentation/.template.config`, copies the same
folder by `--framework`, with `docs/features/presentation.md`; for `avalonia` and `wpf` it also brings
`Src/Clients`, `Tests/Clients` and ADR 0023, which a project without a client lacks. For `worker`
it brings ADR 0025 instead of the page, refuses every `-n` but `Worker` (`workerIsNamedWorker`), a
project without the API (`workerTakesTheJobsOfTheApi`) and SQLite (`workerNeedsServerDatabase`), and
the project's `presentation-join` moves the jobs, as [the worker](#the-worker) says. `--has-api`, true by default, says whether the project holds the
API: its computed `api` conditions the README's lines, and `presentationNeedsTheApi` refuses a
desktop client, or the MCP server under local accounts, in a project without it. It cannot read the project it runs
in, so it declares what the starter's markers read as parameters and computed symbols of the same
names, values, defaults and refusals as `template.json`; `-n` replaces the starter's token and
`--project-name` replaces `AppTemplate` in the two spellings the source name gives it, the name and
its lower-case namespace, and its initials replace the folders' token, as [project folders](#project-folders)
says. `ThePresentationItem_ReadsTheProjectsOptionsAsTheProjectTemplateDeclaresThem`
fails when a symbol the starters read is missing or differs, or when the two templates leave out
different files. What `--presentation` writes outside the project's folders, an item template
cannot edit: the starter's `README.md` lists each of those lines under "Joining a project generated
without one", as an edit placed after or before the lines around it, or in their place, and
`TheStarterReadme_ListsEveryLineItsStarterWritesOutsideItsFolders` fails, for each
starter, until a line a branch of its symbols writes is there: `presentationBlazorServer` or
`anyPresentation` for Blazor, `presentationAvalonia`, `dotnetClient` or `anyPresentation` for
Avalonia, `presentationWpf`, `dotnetClient` or `anyPresentation` for WPF, `presentationMcp` or `anyPresentation` for the MCP server, `jobsWorker` for the worker but the lines of a project without the API, outside the files the item template brings whole and the solution, which
`presentation-scaffold` completes. The Blazor WebAssembly starter's `README.md` answers to the same
rule for `webBlazorWasm` and `dotnetClient`, read by the project's `web-join` task, and
`TheWebReadme_ListsEveryLineItsAppOrItsWorkspaceWritesOutsideItsFolders` holds it for
`Src/Web/apps/FrameworkSite/README.md` (the framework symbols and `webServerRendering`),
`Src/Web/apps/PlaceholderSite/README.md` (`webPlaceholder`) and `Src/Web/README.md` (`anyWeb`, the
lines of a first app's workspace, which `web-join` makes before the app's while `cleanarch.json`
records no workspace), each leaving to the others the lines their symbols write. For an app added to
a workspace, the overview, the first run and the web front end page stay as they are, since they
describe the app the project was generated with; a first app's workspace brings the web front end
page and the end-to-end tests of the web apps whole. A server-rendered app brings ADR 0024 into its own
folder, since the engine refuses to overwrite the copy an earlier one placed, and `web-join` moves it
to `docs/decisions` or drops it when the project holds it already; the app's `README.md` lists the
lines citing it.
Under `externalAuth` the WebAssembly app has code of its own, and its unit tests come from
`.template.config/variants/blazor-wasm-sign-in/`, which both templates copy to the project's root,
since the starter's folder lands in `Src/Web/apps/`; `web-scaffold` adds them to the solution with
the app. A shared file that gains such
a branch gains its edit in the README, with an anchor that is unique in every shape. The project's
`presentation-join` task reads that section and makes the edits, every one or none, so a bullet keeps
the form it reads: ``- in `<path>`, after|before|replace|remove ...:``, then the anchor's fenced
lines, then the added ones.

`presentation-scaffold` installs the template package `scaffold.json` pins into a hive of its own,
runs `dotnet new` with its input closed and, when the template offers `--allow-scripts`, its
post-action scripts declined: `dotnet new` asks again for ever on a closed input, and accepts the
option only from a template that has such a script, so one that would ask fails instead of waiting
for an answer. It keeps the folders and files the descriptor names, a file the starter holds
winning, and refuses a generated project file that declares a package or a property the starter's
lacks. It adds the project to the solution with its `<Name>.Core` sibling when it has one, and the
tests of each. A project that references the .NET client, itself or through that sibling, brings the
client and its tests into the solution with it, and, once the OpenAPI reference exists, `api-client` writes the client. The package is pinned by hand: bump its
`version` with the ASP.NET Core runtime image of the `Dockerfile`s, generate a project with
`--presentation api --presentation blazor-server`, scaffold it, and commit what the scaffold now requires: the
starter keeps its own `Components/App.razor`, a copy of the one the template writes without its `<ImportMap />`, so
resynchronise it with the new version's, and drop that component again.

`presentationNameIsTaken` refuses each `-n` of `cleanarch-presentation` that would make
`<Project>.<Name>` or its test project one of the template's own projects, but `Worker` for the
worker itself;
`ThePresentationItemsNameRefusal_NamesEveryProjectTheTemplateWrites` lists the projects under `Src`
and `Tests` and fails until a new one is refused. Under `wpf` it also refuses each name whose
`<Name>.Core` is one of them, `Infrastructure` and `Presentation` beyond those already refused, and the
same test fails until it does.

## The worker

`--presentation worker` moves the jobs out of the API
([template decision 0012](decisions/0012-where-the-jobs-run-is-an-option.md)), and without the API
it is the host that runs them. The worker's own files
are a starter written whole, `.template.config/starters/worker`
([template decision 0013](decisions/0013-a-starter-the-generator-cannot-add-to-is-written-whole.md)):
its project file, `Program.cs`, settings, `Dockerfile`, caller identity and the compositions of the
directories its passes read, with its test project and `WorkerCompositionTests`. Its `scaffold.json`
is of kind `written`, so `presentation-scaffold` only adds the two projects to the solution and
formats them. The jobs themselves stay where they are in this checkout, the API's `Common/Jobs` and
`Features/*/Jobs` with their unit tests: under `jobsWorker` the API's source leaves them out, or the
API is left out whole, and two
sources of `template.json` copy them under the worker and its tests. Each namespace they declare has
a generated switch symbol, `jobsNamespace<Folder>`, whose value is the API's or the worker's; a job
folder a new feature brings needs one, and `TemplatePackagingTests` fails until it has it. The API's
own files condition what composes the jobs (`AddBackgroundJobs`, `AddAuthMaintenanceJobs`, the
sweep's registration and the `Jobs` settings) on `!jobsWorker`.

A project generated without the worker takes it later: `cleanarch-presentation --framework worker
-n Worker` writes the starter and ADR 0025, and the project's `presentation-join` does what the
template's sources and symbols do at generation. It moves the API's `Common/Jobs` and
`Features/*/Jobs`, and the `Features/*/Jobs` of its unit tests, into the worker and its tests;
renames each namespace they declare, in every file of the project, from `<Project>.Api.` to
`<Project>.Worker.`, the rule `EveryNamespaceTheWorkerTakes_HasItsSwitchSymbol` holds the switch
symbols to; and makes the edits the starter's `README.md` lists for the lines conditioned on
`jobsWorker`, which also set ADR 0011 to `Superseded by` ADR 0025, since a decision is never erased, which `TheStarterReadme_ListsEveryLineItsStarterWritesOutsideItsFolders`
holds. It refuses before writing while git reports a change to a file it would move, edit or remove,
builds the solution and the worker's tests in a copy of the project with the join made, and writes
nothing when that build fails, naming what a job uses that only the API declares. A line no option of
the item can tell apart stays out of that branch: the API-alone sentence of the background jobs page
is conditioned on `!jobsWorker` alone, so that no presentation project's README needs it, and a test
literal that names a job namespace names the worker's, which the rename leaves alone. A launcher test
generates a project with `--presentation api --presentation worker` and the same project without the
worker, adds it with the item template and the join, and requires the two trees to be the same but
for ADR 0011, superseded, and its row in the decisions index.

## Web lockfiles

The repository's `Src/Web/pnpm-lock.yaml` locks every app at once, so it is excluded from generation.
`dotnet run Tools/Tasks.cs web-lockfiles` writes the lockfile of each shape holding a `package.json`
into `.template.config/variants/web-<App>/Src/Web/`; a framework app gets none, since `web-scaffold`
writes it where the app is created. Run the task after any change to the union lockfile; `--check`
fails until you do.

On every pull request, the `Web lockfiles` job of `.github/workflows/template-web-lockfiles.yml` runs
the task, uploads the lockfiles it changed and fails, naming the command. On a Dependabot pull request
of the web workspace, `.github/workflows/template-web-lockfiles-commit.yml` then commits them to the
pull request's branch:

| Facet | How |
|---|---|
| Trigger | `workflow_run` on the failed run, kept only for a `pull_request` run Dependabot started on a `dependabot/npm_and_yarn/` branch of this repository |
| What runs with the write token | nothing from the pull request: no checkout, only `gh api` over the uploaded files, each a regular file at a lockfile path the branch already holds |
| Commit | made through the API on the commit the lockfiles were written for; the branch moves only if it still points there, so a newer push wins and its own run writes them |
| Checks | a commit made with `GITHUB_TOKEN` starts no workflow, so the checks run on the next event: update the branch with `develop`, which a merge requires anyway, or close and reopen the pull request |

The run that writes the lockfiles has a read-only token and runs the pull request's code, as any pull
request's checks do; the run that writes to the repository runs none of it, which `pull_request_target`
checking out the branch would. A personal token or an app would start the checks at once, at the cost
of a secret that can push, which this repository does not hold.

## The .NET client

`Src/Clients/AT.Client` and its tests are built and tested in this checkout, and reach a
generated project only when `dotnetClient` is true, which `--presentation avalonia` and
`--presentation wpf` set, or through `cleanarch-presentation --framework avalonia` or `wpf`. Its session is written under `localAuth`, its
validation problem under `localAuth || anyFeature`, the two shapes whose document holds them.
`dotnetClient` also brings the Kiota entry of the tool manifest; the `api-client` task, the CI step
that runs `api-client --check`, the Dependabot group and the `Generated` exclusions are in every
project and act once `Src/Clients` holds the client. `Generated/` is copied as Kiota wrote it:
`template.json` gives `**/Generated/**.cs` a conditional syntax no line uses, so Kiota's own `#if`
lines survive and only the names are replaced. The tool manifest therefore comes in four shapes:
`.config/dotnet-tools.json` with every tool, and `no-sonar`, `no-client` and `no-sonar-no-client`
under `.template.config/variants/`, each the full manifest minus its tools, which
`EachVariantToolManifest_IsTheToolManifestMinusItsTools` holds. A Kiota bump changes the full
manifest and the `no-sonar` one, and its pull request runs `dotnet run Tools/Tasks.cs api-client`
and commits what it writes.

## The project file

`cleanarch.json` records what a project was generated with: the template package and version, the
project's name, every option that is not a part under its value, `client` when `Src/Clients`
holds the .NET client, the web workspace with its apps and packages, and the presentation project
([template decision 0017](decisions/0017-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md)).
This checkout holds the file the defaults generate; `Tools/Tasks.cs` finds the root by it and reads
the options there rather than through markers.

| Facet | Where |
|---|---|
| Values | one generated symbol per recorded value, whose `replaces` names the key with the checkout's value, such as `"auth": "local"`, so that no other file holds the token; the name, the version, the scope and the part names follow the tokens the rest of the tree uses |
| Parts | `.template.config/variants/project-file-web`, `.template.config/variants/project-file-presentation` and `.template.config/variants/project-file-web-presentation`, each the checkout's file plus its parts, copied in its place by `anyWeb` and `anyPresentation` |
| Additions | `presentation-join` and `web-join` record the part they join, `client` with it, and `web-join` a package of the workspace; every writer keeps the key order and any key it does not know |
| Tests | `ProjectFileTests` holds the file to the tree in every project; `TemplatePackagingTests` holds the checkout's file to the defaults, each variant to it, and every value to a symbol that replaces it; `CheckProjectFile.cs` holds each leg's file to its options |

In this checkout `Src/Web`, `Src/Clients` and the API's external-identity mapping are the sources of
shapes the defaults leave out, so `ProjectFileTests` reads them as absent, in a `templateMaintenance`
block. A new option that is not a part gets its key in `cleanarch.json` and in the three variants,
a generated symbol that replaces it, its place in the list of `.template.config/acceptance/CheckProjectFile.cs`, and a check in
`ProjectFileTests` when the tree shows it.

## Project folders

A project's folder carries the project's initials in place of its name
([ADR 0029](../decisions/0029-a-project-folder-carries-the-products-initials-not-its-name.md)), so
this checkout's folders start with `AT.` and its project files with `AppTemplate.`. The two
generated symbols `projectFolderPrefixAnyCase` and `projectFolderPrefix` compute the initials from the
name and replace `AT.` in every path and file; `cleanarch-presentation` and `cleanarch-webapp`
declare the same two from `--project-name`. The tasks, the generators, the launcher and the
architecture tests each compute a folder with a `SegmentInitials` of their own, since a file-based
program shares no code.

| Rule | Test |
|---|---|
| The three templates compute the same initials as the tools, for names with and without capitals | `TemplatePackagingTests.TheFolderSymbol_GivesTheProductsInitials_AsTheToolsComputeThem` |
| Every `SegmentInitials` reads as the architecture tests' own | `TemplatePackagingTests.EveryCopyOfTheInitials_ReadsAsTheArchitectureTestsOwn` |
| The token stands only before a project's folder, at the start of a word, in every path and file a template writes | `TemplatePackagingTests.TheFolderToken_NamesNothingButAProjectFolder` |
| No path under `Src` and `Tests` passes 170 characters under the default name | `TemplatePackagingTests.TheLongestPathUnderSrcAndTests_StaysWithinItsBudget_UnderTheDefaultNameAppTemplate` |

## The generators' shared code

`Tools/NewFeature.cs`, `Tools/NewUseCase.cs`, `Tools/NewSetting.cs` and `Tools/NewEvent.cs` are
file-based programs, which share no code, so each closes on its own copy of what they hold in
common, and `Tools/NewFeature.cs` holds the copy the others read as. A change to a shared unit is
made in `Tools/NewFeature.cs` and copied as is.

| Unit | Copied in | Test |
|---|---|---|
| `TextEdits`, the edits in files a generator does not own, placed beside a line or computed on the whole text, and the write of every file or none | the four generators | `TemplatePackagingTests.EveryCopyOfTheSharedFileEdits_ReadsAsNewFeaturesOwn` |
| `CSharpNames`, a name in PascalCase and the C# keywords it must not become once lowered | the four generators | `TemplatePackagingTests.EveryCopyOfTheNameRules_ReadsAsNewFeaturesOwn` |
| The field model and `FieldGrammar`, with the subset of each `FieldProfile` | `Tools/NewFeature.cs` and `Tools/NewEvent.cs` | `TemplatePackagingTests.EveryCopyOfTheFieldGrammar_ReadsAsNewFeaturesOwn` |

`new-setting --keys` reads its own grammar: a key is a bounded configuration value with a default,
`duration` and `secret` among its types, which no field has; its names follow `CSharpNames`.

## The package

`.template.config/TemplatePackage.csproj` packs the four templates into one NuGet package,
`CleanArch.WebApi.Templates`; its `PackageId` is the one place that names it.

```bash
dotnet pack .template.config/TemplatePackage.csproj --output artifacts/package
```

| Facet | Where |
|---|---|
| Content | every file of the checkout under `content/`, minus what generation always leaves out, the scratch folders (`.git`, `.claude`, `bin`, `obj`, `node_modules`, `TestResults`, `artifacts`), the plans and the package project; `TemplatePackagingTests` holds the list against `template.json` |
| Version | the `templateVersion` symbol of `.template.config/template.json`, which release-please raises, and nowhere else; it replaces `0.0.0-checkout` in a generated project's overview and its `cleanarch.json`, as [template decision 0018](decisions/0018-a-project-keeps-its-provenance-and-its-tool-from-the-template.md) allows |
| Metadata | id, authors, description, tags, licence, the repository `README.md` and `.template.config/icon.png`, all in the package project |

## Branches and releases

| Branch | Holds | Moves by |
|---|---|---|
| `develop` | the next version; the default branch | a pull request from a `<type>/<subject>` branch, up to date with `develop` and rebased, the history kept linear |
| `main` | the released commits only | `template-release.yml` fast-forwarding it to each release; nobody pushes to it |
| `net<major>.0` | the last release built on an earlier .NET major, for its fixes | a pull request, cut from that major's last tag when `develop` moves to the next .NET |

Every commit is a [conventional commit](https://www.conventionalcommits.org/en/v1.0.0/): a `feat` or a
`fix` lands in the changelog, a `!` or a `BREAKING CHANGE` footer makes the next version a major one,
and the other types stay out of it. A working branch is kept after its merge.

A squashed pull request lands under its title, which release-please skips in silence unless it is a
conventional commit. The `Conventional Commits title` job of
`.github/workflows/template-pull-request-title.yml` checks the title of each pull request to
`develop`, again whenever it is edited: `<type>[(<scope>)][!]: <subject>`, the type one of the
changelog sections of `.github/release-please/config.json` (`feat`, `fix`, `perf`, `revert`, `docs`,
`refactor`, `test`, `build`, `ci`, `chore`, `style`), the scope in lower case. Its step first proves
the pattern accepts the titles release-please and Dependabot write and refuses a few that are not.

The checks run on the pull request, which must be up to date with `develop`, so the merged tree is
the one they passed. A push to `develop` runs CI, CodeQL and the release workflow; the acceptance
matrix runs in full each night `develop` has moved, as its pull request ran only the legs its change
could affect. The SonarQube steps of `ci.yml` analyse a push to the default branch only, as
SonarCloud's free plan analyses one branch, so the SonarCloud project names `develop` as its main
branch; they skip a Dependabot pull request, which gets no repository secret. Dependabot reads its configuration on the
default branch, and `.github/dependabot.yml` names `develop` for each ecosystem in this repository
only.

`.github/workflows/template-release.yml` runs release-please on every push to `develop`, from
`.github/release-please/config.json`; both reach neither a project nor the package. It keeps one
pull request open that raises `templateVersion` and adds the version's `CHANGELOG.md` entry from the
commits since the last release. Merging that pull request is the release: release-please tags
`v<version>` and creates the GitHub Release, and the same workflow then checks out the tag, refuses
it if it does not name `templateVersion`, packs the template package, the launcher and
`CleanArch.Cli`, which a project's bootstrap pins at its template's version, installs the
template package alone in a scratch hive, generates and builds the defaults, generates them again
through the launcher, attests the packages' provenance and attaches them to the release. Its
`publish` job waits in the `nuget` environment for a reviewer, exchanges the workflow's identity
for a short-lived nuget.org key (Trusted Publishing, with `NUGET_USER` the account's name as a
repository variable) and pushes the three packages; a version already on nuget.org fails the push. Its
`mirror` job then pushes the same packages to GitHub Packages, so the repository page lists them
([template decision 0007](decisions/0007-github-packages-mirrors-nuget-org.md)). Last,
`promote` fast-forwards `main` to the release commit, and refuses if `main` is not its ancestor.
[Template decision 0006](decisions/0006-develop-integrates-main-holds-the-releases.md) gives the
reasons.

## The launcher

`Launcher/CleanArch.Create` is the .NET tool `dnx CleanArch.Create` runs, with its tests in
`Launcher/CleanArch.Create.Tests`. The folder is its own solution, `Launcher/CleanArch.Create.slnx`,
which holds four more projects. Generation and the template package leave the folder out, and
`TemplatePackagingTests` holds both.

| Project | What it is |
|---|---|
| `Launcher/CleanArch.Tooling` | the library both tools share, never packed on its own: running a process and its verbatim output, the terminal, the templates' manifests with the evaluation of their refusals, and the questions asked from them; its tests are in `Launcher/CleanArch.Tooling.Tests` |
| `Launcher/CleanArch.Cli` | the .NET tool `dotnet cleanarch`, packed as `CleanArch.Cli` at the same `templateVersion`, which finds a project by the project file at its root; its tests are in `Launcher/CleanArch.Cli.Tests` |

[Template decision 0017](decisions/0017-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md) and
[0019](decisions/0019-the-launcher-creates-and-the-project-tool-adds.md) say what each tool is for.
The tests that read the manifests through the launcher's own settings stay in
`Launcher/CleanArch.Create.Tests`.

| Facet | Where |
|---|---|
| Questions | read from the manifests under `.template.config/`, embedded at build; [template decision 0004](decisions/0004-the-launcher-reads-the-templates-manifests.md) gives the rules an option and a refusal keep |
| Version | the same `templateVersion`; the launcher installs `CleanArch.WebApi.Templates` at its own version |
| Thread | drawn with the glyphs every console font carries; [template decision 0016](decisions/0016-the-launcher-draws-only-glyphs-every-console-font-carries.md) gives the rule and the test that holds it |
| Packages | `Spectre.Console` and `System.CommandLine`, pinned in `Launcher/Directory.Packages.props`, which imports the repository's |
| Gate | `verify` restores, formats, builds and tests the launcher's solution, all five projects, after the project's gate; CI's `launcher` job does the same |
| Release | `template-release.yml` packs it beside the template package, with its symbol package, installs it as a tool, generates the defaults with it, and publishes it with the template package |

Inside a generated project its item commands read the options the project was generated with from
its files rather than asking them; `GeneratedProjectTests` holds each reading on a tree of its own:

| Option | Read from |
|---|---|
| `-n` | the one solution file beside `Tools/Tasks.cs` |
| `--auth` | `local` with the `Application.Auth` project, `external` with the JWT bearer package in `Directory.Packages.props`, `none` otherwise |
| `--examples` | `all` with `Features/TodoLists` in the application project, `notes` with `Features/Notes` alone, `none` otherwise |
| `--files` | the `Infrastructure.Storage` project |
| `--database` | the provider package `Infrastructure.Core` references: SQL Server, SQLite, PostgreSQL otherwise |
| `--ci`, `--agent-files` | `.github/workflows/ci.yml`, `AGENTS.md` |
| `--has-api` | the `Src/Presentation/<Initials>.Api` folder |
| `--scope`, `--workspace` | the name in `Src/Web/package.json`, the project's name in kebab case without one; `Src/Web` |

`presentation` runs `cleanarch-presentation`, then the project's `presentation-join` and
`presentation-scaffold` tasks, and stops before the second when the first fails. `web-app` and
`web-package` run the project's `web-join` after the item template, with the framework, audience
and rendering asked, so the lines a web app's `README.md` lists are made and `cleanarch.json`
records every part the launcher adds.

Its tests read this checkout's manifests: the options are the visible ones, each refusal fires for
some combination, the launcher refuses exactly where `refusedOptions` does, and answering only what
it offers never ends in a refusal. The terminal thread is tested on Spectre's `TestConsole`, by its
markers and their order rather than its colours, with the processes it starts replaced. One test packs the
templates, runs the launcher without a terminal for a project, its first web app, a package and a
presentation project, and compares the tree with what `dotnet new` writes from the same answers with
`--presentation api --presentation blazor-server`: the lines `presentation-join` places by the
anchors of the starter's `README.md` land where the project template writes them, so an anchor
moved, or a block of the project template moved away from its anchor, fails it. It stops before
`presentation-scaffold`, which fetches Microsoft's template package from NuGet. Another generates a
project with `--web`, once as a React single-page app and once as an Angular app rendered on the
server under an external provider, and the same project without it, to which `cleanarch-webapp
--workspace` and `web-join` add the app: the two trees must match but for the options the overview
records, so an edit of `Src/Web/README.md` or of the app's `README.md` that is missing, wrong or
placed elsewhere than the project template writes its lines fails it. A third adds two
server-rendered apps to a project generated with a single-page app and requires ADR 0024, its index
row and the lines citing it as `--web-rendering ssr` writes them, once.

A multi-valued option, `--presentation`, is one multi-select that keeps at least one value and
reaches `dotnet new` as the option repeated once per value; the launcher's conditions read `==` as
"among the values" and `!=` as "not among them", splitting an answer on `|`, which no free-text
answer a condition compares may hold. When a web app, a desktop client or the MCP server under
local accounts calls the API, the launcher shows the API as answered for you, naming the caller,
and passes it; `TheCallersTheLauncherNames_AreExactlyWhatMakesTheTemplateAddTheApi` holds the
callers against `needsApi`. When the hosts chosen hold neither the API nor the worker, it shows the
worker as answered for you and passes it; `TheWorkerTheLauncherAdds_IsExactlyWhatMakesTheTemplateAddIt`
holds that rule against `needsWorker`. Inside a project, `presentation` asks the framework, then the name,
proposing the framework's own (`Portal`, `Mcp`, `Desktop`, `Worker`).

```bash
dotnet run --project Launcher/CleanArch.Create -- --template-source . --hive /tmp/cleanarch-hive
```

runs it from the checkout; pass `--template-source` a `.nupkg` to try a packed one.

## Changelog

`CHANGELOG.md` follows Keep a Changelog and stays here. Record what changes a generated project: a
layer's shape, a rule the tests hold, a setting, a default that affects security or deployment. A major
version means adopting the change in an existing project takes manual work.

## Decisions

- [0001. Options remove parts and refuse combinations](decisions/0001-options-remove-parts-and-refuse-combinations.md)
- [0002. Nothing about the template reaches a project](decisions/0002-nothing-about-the-template-reaches-a-project.md), superseded by 0018
- [0003. Framework apps come from official CLIs](decisions/0003-framework-apps-come-from-official-clis.md), superseded by 0008
- [0004. The launcher reads the templates' manifests](decisions/0004-the-launcher-reads-the-templates-manifests.md)
- [0005. A project carries one database provider](decisions/0005-a-project-carries-one-database-provider.md)
- [0006. develop integrates, main holds the releases](decisions/0006-develop-integrates-main-holds-the-releases.md)
- [0007. GitHub Packages mirrors nuget.org](decisions/0007-github-packages-mirrors-nuget-org.md)
- [0008. A web app comes from a starter](decisions/0008-a-web-app-comes-from-a-starter.md)
- [0009. A .NET project comes from a starter](decisions/0009-a-dotnet-project-comes-from-a-starter.md)
- [0010. A pull request runs the legs its change can affect](decisions/0010-a-pull-request-runs-the-legs-its-change-can-affect.md)
- [0011. A starter may render on the server](decisions/0011-a-starter-may-render-on-the-server.md)
- [0012. Where the jobs run is an option](decisions/0012-where-the-jobs-run-is-an-option.md)
- [0013. A starter the generator cannot add to is written whole](decisions/0013-a-starter-the-generator-cannot-add-to-is-written-whole.md)
- [0014. A Windows-only shape is proved on a Windows runner](decisions/0014-a-windows-only-shape-is-proved-on-a-windows-runner.md)
- [0015. An MCP server is a presentation project, written whole](decisions/0015-an-mcp-server-is-a-presentation-project-written-whole.md)
- [0016. The launcher draws only glyphs every console font carries](decisions/0016-the-launcher-draws-only-glyphs-every-console-font-carries.md)
- [0017. A project runs its tasks from a pinned .NET tool](decisions/0017-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md)
- [0018. A project keeps its provenance and its tool from the template](decisions/0018-a-project-keeps-its-provenance-and-its-tool-from-the-template.md), supersedes 0002
- [0019. The launcher creates, and the project's tool adds](decisions/0019-the-launcher-creates-and-the-project-tool-adds.md), replaces a bullet of 0004
- [0020. A project is the presentation projects it holds](decisions/0020-a-project-is-the-presentation-projects-it-holds.md)
