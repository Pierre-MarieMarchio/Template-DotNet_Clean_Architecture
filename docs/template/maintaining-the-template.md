# Maintaining the template

This repository builds, tests and runs as the default generation does: every condition is a comment
the compiler ignores. A change is done when the project's gate passes here and every generated shape
still holds.

## The template gates

`dotnet run Tools/Tasks.cs verify` runs the project's gate plus what only the template carries:

| Gate | Checks |
|---|---|
| `Tools/CheckDocPaths.cs` | every repository path in a Markdown code span exists, and every `docs/` path a workflow, script or `.http` file names; `CHANGELOG.md` and the plans folder are exempt |
| `Tools/CheckWorkflows.cs` | what a YAML parse misses: a dangling `needs:`, an action on a mutable tag, an image without a digest, a missing `permissions:`, an undeclared `$VAR`, a path absent from disk |
| `Tools/CheckComments.cs` | no comment outside the allowlist, one guidance line at a time in configuration files, no Markdown narrating the repository's history |
| `web-lockfiles --check` | each generated web shape's lockfile equals what the pinned pnpm writes for it; needs pnpm (`corepack enable`) |

Each gate runs its `--self-test` first, against fixtures that must make it fire and fixtures that
must leave it silent, and fails when it examined nothing. A rule added to a gate comes with the fixture
that fails without it. These three files are excluded from generation, and the project's `hygiene`
task asks the filesystem which gates its tree carries, so one task serves both.

CI runs them in `.github/workflows/ci.yml`, under `templateMaintenance` blocks a generated project
does not receive. Its end-to-end job skips its one step in this repository, whose stack includes web
apps no CLI has created yet; the acceptance matrix runs the suite instead.

## Migrations in this checkout

The template ships no migration, so this checkout has none until
`dotnet run Tools/Tasks.cs migration-initial` creates them, as bootstrap does in a project; the
tests, `verify` and the drift check need them. They are git-ignored here only, by a `templateMaintenance` block of `.gitignore`, and
both `template.json` and `.template.config/TemplatePackage.csproj` exclude them, so a project
generated from this checkout or from its package starts without them; `TemplatePackagingTests` holds
the three. After a model change, delete each context's `Migrations` folder and run the task again. The
CI, Sonar and release workflows run it before building, under `templateMaintenance` blocks.

## The acceptance matrix

`.github/workflows/template-acceptance.yml` packs the template package once, and per leg installs
that `.nupkg`, generates under another name, asserts the source name leaked nowhere, bootstraps
(the formatting, then `migration-initial` after asserting no migration was generated, and once more to
prove it creates nothing), builds with warnings as errors, runs the Docker-free tests, checks every generated context for
pending model changes, runs the generated project's `hygiene`, and checks every path its pages cite
with this checkout's `Tools/CheckDocPaths.cs`. The defaults leg and the smallest project's leg also
run the integration suites, on the runner's Docker through Testcontainers, and the defaults leg then
runs the generated project's `dotnet run Tools/Tasks.cs end-to-end` against its Compose stack. The defaults
leg, the local-auth leg without examples and the external-auth leg without examples also run the
generator round trip: `new-feature` writes an owner feature, a shared one, an aggregate one and an
aggregate one on `get,list,create` in turn, the aggregates after `new-use-case` adds a command, a
query and a command without input to each while refusing the others.
`.template.config/acceptance/ApplyFeatureEdits.cs` then makes the hand edits `new-feature` printed,
read from its output; the whole solution builds with warnings as errors, the architecture tests pass,
and the feature's own tests pass in every Docker-free test project. The edits are undone and
`remove-feature --apply` leaves `git status` empty. Then all four are written together, proved the
same way, `hygiene` passes, and they are removed last in first out. In a project generated without an
owner feature, the first one prints edits as prose, its account-deletion wiring among them, so there
only the domain and application layers are built and tested. A web leg also checks its
lockfile, adds a second app and a package with the item templates, scaffolds, runs the workspace's
install, lint, test and build, fails when an app with a build script has no built page, checks a
second `web-scaffold` changes nothing, starts the whole stack and asks each app for its page, its
headers and an answer only the API gives; it fails when a served page holds an event-handler
attribute or an inline script whose hash the served `script-src` lacks, and reports its inline
styles. The every-starter leg adds each framework starter its first app is not, so one workspace
proves them all together. Every leg runs `presentation-scaffold` before the formatting, a step that
does nothing without a presentation project; the three web legs that pass `--presentation
blazor-server`, one per authentication mode, create it, build and test it, and ask it in the stack
for its page and `/health`. The leg that adds a first app adds the presentation project too, with
`cleanarch-presentation`, makes the edits its `README.md` lists with
`.template.config/acceptance/ApplyPresentationEdits.cs`, and from there runs as those three do.

| Leg | Options |
|---|---|
| defaults | none |
| auth none, smallest project | `--auth none --examples none --files false` |
| external auth, notes | `--auth external --examples notes` |
| local auth, no files, no ci, no sonar | `--auth local --examples none --files false --ci false --sonar false` |
| external auth, all examples | `--auth external --examples all --files true` |
| external auth, no examples, no files | `--auth external --examples none --files false` |
| web placeholder, smallest project | `--web placeholder --auth none --examples none --files false --presentation blazor-server` |
| sql server, external auth, all examples | `--database sqlserver --auth external --examples all --files true` |
| sqlite, defaults | `--database sqlite` |
| sqlite, smallest project | `--database sqlite --auth none --examples none --files false` |
| sqlite, external auth, all examples | `--database sqlite --auth external --examples all --files true` |
| web react, local auth | `--web react --presentation blazor-server` |
| web angular, external auth, notes | `--web angular --auth external --examples notes --presentation blazor-server` |
| web react back office, smallest local project | `--web react --web-audience admin --examples none --files false` |
| web react, then an angular back office | `--web react`, then `cleanarch-webapp --framework angular --audience admin` |
| no web, then a react app and its workspace | none, then `cleanarch-webapp --framework react --workspace` and `cleanarch-presentation --framework blazor-server` |
| web, every starter | `--web vue --examples none --files false`, then `cleanarch-webapp` once per other framework starter, each on its own `--port` |

Every leg also greps its project for the other database providers' packages and types, reading its own
`database`; the SQLite legs run their integration suites on a file and start no database container, and
the SQL Server leg starts one large image, so it has the longest timeout. A leg without a web app asserts no workflow, Dependabot entry, Compose file or ignore file names a
workspace. The leg that adds a first app runs that assertion, then `cleanarch-webapp --workspace` and
the lines `Src/Web/README.md` lists, and is a web leg from there. `--workspace` only adds files, and
the engine refuses to overwrite one, so everything a web app needs from the API ships with every
project and the web jobs are `web.yml`, not jobs of `ci.yml`. A separate job asserts each refused combination exits non-zero and writes no file. The legs
are the shapes the options produce, not their cross product, and run with `fail-fast: false`.

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
| `Src/Infrastructure/AppTemplate.Infrastructure.Core/Common/Contexts/DatabaseProvider.cs` | `DesignTimeConnectionString`, `VersionColumnName`, both `UseDatabase` overloads (design time, and run time with `DatabaseOptions` and `ContextMigrations`), `ApplyDatabaseConventions`, `IsVersion`, `IncludingColumns`, `WhereColumnEquals`, `IsUniqueViolation`, `DedicatedConnectionString` |
| `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Leases/LeaderLease.cs` | `LeaderLease`, the `ILeaderLease` adapter, constructed from `IConfiguration` and a logger |
| `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Files/Adapters/Locks/OwnerQuotaLock.cs` | `OwnerQuotaLock`, constructed from the connection string and a logger, throwing `OwnerQuotaLockTimeoutException` after `WaitLimit` |
| `Src/Presentation/AppTemplate.Api/Common/Observability/DatabaseInstrumentation.cs` | the provider's tracing and metrics, or none |
| `docker-compose.database.yml` | the `db` service and the `database-client` service whose environment gives the api `ConnectionStrings__Default`; SQLite declares only `database-client`, which mounts the `db-data` volume |
| `Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/DatabaseServer.cs`, `Tests/Integration/AppTemplate.Infrastructure.Auth.IntegrationTests/Fixtures/DatabaseServer.cs` | the test server: `Image` (one `docker-compose.database.yml` runs), `ConnectionString`, `StartAsync`, `DisposeAsync`, and `Table(schema, table)`, the name a test's raw SQL uses; the API one adds `TablesSql`, `ResetSql`, `SchemasSql`, `HistorySchemasSql(table)`, and `Value` and `Literal` for a `Guid` or a `DateTimeOffset` as the provider stores it; the Auth one adds `TransientFault` |
| `Tests/Infrastructure/AppTemplate.Infrastructure.Core.UnitTests/Common/Contexts/DatabaseProviderTests.cs`, `Tests/Infrastructure/AppTemplate.Infrastructure.Auth.UnitTests/Fixtures/DatabaseFaults.cs`, `Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/Features/Files/Adapters/Locks/OwnerQuotaLockTests.cs`, `Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/PersistenceModuleConnectionStringTests.cs` | the provider's own unit tests and faults; an overlay may leave one out by a `template.json` exclusion |

A provider without `xmin` registers `VersionStampingSaveChangesInterceptor` from its run-time
`UseDatabase`, which the modules call after the saving interceptors; `IsVersion` makes `Version` a
plain concurrency token. A provider without schemas adds `SchemaAsTablePrefixConvention` in
`ApplyDatabaseConventions` and names each history table with its `TableName`. Both are in-tree and
tested on SQLite here, and shipped only under `versionCounter` and `tablePrefixes`.

The shared files carry every provider's lines already: the package references of
`Src/Infrastructure/AppTemplate.Infrastructure.Core/AppTemplate.Infrastructure.Core.csproj` and of the
two integration projects, the versions in `Directory.Packages.props`, the variables of `.env.example`,
and the `developmentConnectionString` switch that rewrites `appsettings.Development.json`. An overlay's
compose file uses the variable names `.env.example` gives it.

A provider is refused until its overlay is wired. Wiring one is three edits to `template.json`: a
source copying the folder under its computed symbol, the removal of its `NotYetGenerated` refusal with
its `dotnetcli.host.json` entry, and its removal from `refusedOptions`. `TemplatePackagingTests`
fails until the three agree with the folder. Its refusal line in the acceptance refusal job becomes a
leg, and each leg's provider-leak check reads the leg's `database`.

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
   shown or an option missing there.
2. Exclude whole paths with a source modifier under a condition; condition content inside a file in
   that file's own comment syntax.
3. A combination that cannot hold together is a hidden required parameter, and the same condition in
   `refusedOptions`, so the `#error` variant catches a hand-passed bypass.
4. Add the leg that generates the new shape, and a refusal to the refusal job.
5. Condition every page whose truth changes, and judge each in every form it can take.

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
- Text that names a token (`AppTemplate`, `app-template`, the web app tokens) is replaced in a
  generated project; write a token only where its replacement reads right.
- Project pages say "this project"; only `docs/template/` and the repository `README.md` say "the
  template".

Read a page in the default form, in `--auth none --examples none --files false`, and in each form its
markers distinguish: no broken link or anchor, no marker left, no orphan punctuation, no page about an
absent module.

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
([template decision 0008](decisions/0008-a-web-app-comes-from-a-starter.md)): `angular`, `vue`,
`vite-react`, `vite-vue`, `vite-svelte` and `react-router`, each holding
`FrameworkSite/scaffold.json` and `FrameworkSite/overlay/`, which both templates copy into the app's
folder by the framework chosen and `web-scaffold` consumes at bootstrap. Neither template copies anything else from there. `react`
has no folder: it is an alias of `vite-react`, chosen by the same computed symbol, `webViteReact`,
so it copies the same starter.

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
   both templates agree with the folder. An alias is a choice without a folder, added to the
   computed symbol of the value it repeats.
3. Condition the wiring only where the new framework differs, and the README sentences that name
   its CLI and list what the starter adds. Give the value its row in
   `docs/getting-started/overview.md` and `docs/template/using-the-template.md`, and its name where
   `docs/features/web-frontend.md`, `Src/Web/README.md` and the repository `README.md` list the
   values.
4. Give it an accent colour in `Launcher/CleanArch.Create/Prompts/GuidedConversation.cs`; its
   question comes from the manifests.
5. Add it to the every-starter leg of the acceptance matrix, with a port of its own, which
   scaffolds, builds and serves it beside the others and refuses an inline script its served policy
   does not allow by hash; a starter that differs in the wiring gets a leg of its own as well, unless
   the every-starter leg already proves what differs, as it does for React Router's hash step.

## Presentation starters

A `--presentation` value is a starter too, of kind `dotnet`
([template decision 0009](decisions/0009-a-dotnet-project-comes-from-a-starter.md)):
`.template.config/starters/blazor-server` holds the project's own files at their paths in a project:
`.template.config/starters/blazor-server/Src/Presentation/AppTemplate.PresentationApp` (its `scaffold.json`, project file,
`Program.cs`, settings, `Dockerfile` and wiring), and its test project beside them under `Tests/`; the
project template copies the folder into the project's root under `presentationBlazorServer`. `PresentationApp` is the token
`--presentation-name` replaces, with `presentation-app` and `PRESENTATION_APP` beside it. The
starter's C# is compiled only in a generated project, so a change to it is proved by generating one,
by the three acceptance legs that pass `--presentation` and by the leg that adds one with
`cleanarch-presentation`.

`cleanarch-presentation`, in `.template.config/presentation/.template.config`, copies the same
folder by `--framework`, with `docs/features/presentation.md`. It cannot read the project it runs
in, so it declares what the starter's markers read as parameters and computed symbols of the same
names, values, defaults and refusals as `template.json`; `-n` replaces `PresentationApp` and
`--project-name` replaces `AppTemplate` in the two spellings the source name gives it, the name and
its lower-case namespace. `ThePresentationItem_ReadsTheProjectsOptionsAsTheProjectTemplateDeclaresThem`
fails when a symbol the starters read is missing or differs, or when the two templates leave out
different files. What `--presentation` writes outside the project's folders, an item template
cannot edit: the starter's `README.md` lists each of those lines under "Joining a project generated
without one", as an edit placed after or before the lines around it, or in their place, and
`ThePresentationReadme_ListsEveryLineThePresentationWritesOutsideItsFolders` fails until a line a
`presentationBlazorServer` or `anyPresentation` branch writes is there. A shared file that gains such
a branch gains its edit in the README, with an anchor that is unique in every shape.

`presentation-scaffold` installs the template package `scaffold.json` pins into a hive of its own,
runs `dotnet new` with its input closed and, when the template offers `--allow-scripts`, its
post-action scripts declined: `dotnet new` asks again for ever on a closed input, and accepts the
option only from a template that has such a script, so one that would ask fails instead of waiting
for an answer. It keeps the folders the descriptor names and refuses a generated project file that
declares a package or a property the starter's lacks. The package is pinned by hand: bump its
`version` with the ASP.NET Core runtime image of the `Dockerfile`s, generate a project with
`--presentation blazor-server`, scaffold it, and commit what the scaffold now requires.

`presentationNameIsTaken` refuses each name that would make `<Project>.<Name>` or its test project one
of the template's own projects, as `--presentation-name` in `template.json` and as `-n` in
`cleanarch-presentation`; `ThePresentationNameRefusal_NamesEveryProjectTheTemplateWrites` lists
the projects under `Src` and `Tests` and fails until a new one is refused by both.

## Web lockfiles

The repository's `Src/Web/pnpm-lock.yaml` locks every app at once, so it is excluded from generation.
`dotnet run Tools/Tasks.cs web-lockfiles` writes the lockfile of each shape holding a `package.json`
into `.template.config/variants/web-<App>/Src/Web/`; a framework app gets none, since `web-scaffold`
writes it where the app is created. Run the task after any change to the union lockfile, Dependabot's
included; `--check` fails until you do.

## The package

`.template.config/TemplatePackage.csproj` packs the four templates into one NuGet package,
`CleanArch.WebApi.Templates`; its `PackageId` is the one place that names it.

```bash
dotnet pack .template.config/TemplatePackage.csproj --output artifacts/package
```

| Facet | Where |
|---|---|
| Content | every file of the checkout under `content/`, minus what generation always leaves out, the scratch folders (`.git`, `.claude`, `bin`, `obj`, `node_modules`, `TestResults`, `artifacts`), the plans and the package project; `TemplatePackagingTests` holds the list against `template.json` |
| Version | the `templateVersion` symbol of `.template.config/template.json`, which release-please raises, and nowhere else; it replaces `0.0.0-checkout` in a generated project's overview, as [template decision 0002](decisions/0002-nothing-about-the-template-reaches-a-project.md) allows |
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

The checks run on the pull request, which must be up to date with `develop`, so the merged tree is
the one they passed and a push to `develop` does not run them again: it runs CodeQL, SonarQube and
the release workflow. SonarQube analyses a push to the default branch only, as SonarCloud's free
plan analyses one branch, so the SonarCloud project names `develop` as its main branch; it skips a
Dependabot pull request, which gets no repository secret. Dependabot reads its configuration on the
default branch, and `.github/dependabot.yml` names `develop` for each ecosystem in this repository
only.

`.github/workflows/template-release.yml` runs release-please on every push to `develop`, from
`.github/release-please/config.json`; both reach neither a project nor the package. It keeps one
pull request open that raises `templateVersion` and adds the version's `CHANGELOG.md` entry from the
commits since the last release. Merging that pull request is the release: release-please tags
`v<version>` and creates the GitHub Release, and the same workflow then checks out the tag, refuses
it if it does not name `templateVersion`, packs the template package and the launcher, installs the
template package alone in a scratch hive, generates and builds the defaults, generates them again
through the launcher, attests both packages' provenance and attaches them to the release. Its
`publish` job waits in the `nuget` environment for a reviewer, exchanges the workflow's identity
for a short-lived nuget.org key (Trusted Publishing, with `NUGET_USER` the account's name as a
repository variable) and pushes both packages; a version already on nuget.org fails the push. Its
`mirror` job then pushes the same packages to GitHub Packages, so the repository page lists them
([template decision 0007](decisions/0007-github-packages-mirrors-nuget-org.md)). Last,
`promote` fast-forwards `main` to the release commit, and refuses if `main` is not its ancestor.
[Template decision 0006](decisions/0006-develop-integrates-main-holds-the-releases.md) gives the
reasons.

## The launcher

`Launcher/CleanArch.Create` is the .NET tool `dnx CleanArch.Create` runs, with its tests in
`Launcher/CleanArch.Create.Tests` and its own solution, `Launcher/CleanArch.Create.slnx`. Generation
and the template package leave the folder out, and `TemplatePackagingTests` holds both.

| Facet | Where |
|---|---|
| Questions | read from the manifests under `.template.config/`, embedded at build; [template decision 0004](decisions/0004-the-launcher-reads-the-templates-manifests.md) gives the rules an option and a refusal keep |
| Version | the same `templateVersion`; the launcher installs `CleanArch.WebApi.Templates` at its own version |
| Packages | `Spectre.Console`, pinned in `Launcher/Directory.Packages.props`, which imports the repository's |
| Gate | `verify` restores, formats, builds and tests the launcher's solution after the project's gate; CI's `launcher` job does the same |
| Release | `template-release.yml` packs it beside the template package, with its symbol package, installs it as a tool, generates the defaults with it, and publishes it with the template package |

Its tests read this checkout's manifests: the options are the visible ones, each refusal fires for
some combination, the launcher refuses exactly where `refusedOptions` does, and answering only what
it offers never ends in a refusal. The terminal thread is tested on Spectre's `TestConsole`, by its
markers and their order rather than its colours, with the processes it starts replaced. One test packs the
templates, runs the launcher without a terminal for a project, its first web app and a package, and
compares the tree with what `dotnet new` writes from the same answers.

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
- [0002. Nothing about the template reaches a project](decisions/0002-nothing-about-the-template-reaches-a-project.md)
- [0003. Framework apps come from official CLIs](decisions/0003-framework-apps-come-from-official-clis.md), superseded by 0008
- [0004. The launcher reads the templates' manifests](decisions/0004-the-launcher-reads-the-templates-manifests.md)
- [0005. A project carries one database provider](decisions/0005-a-project-carries-one-database-provider.md)
- [0006. develop integrates, main holds the releases](decisions/0006-develop-integrates-main-holds-the-releases.md)
- [0007. GitHub Packages mirrors nuget.org](decisions/0007-github-packages-mirrors-nuget-org.md)
- [0008. A web app comes from a starter](decisions/0008-a-web-app-comes-from-a-starter.md)
- [0009. A .NET project comes from a starter](decisions/0009-a-dotnet-project-comes-from-a-starter.md)
