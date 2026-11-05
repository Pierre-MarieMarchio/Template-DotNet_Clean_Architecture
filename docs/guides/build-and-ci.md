---
type: guide
title: Build and CI
description: Which task builds, checks and tests this project, what the gate holds, and what each workflow runs.
---

# Build and CI

## The task launcher

`Tools/Tasks.cs` wraps the `dotnet` and `docker` commands of this project and prints each one before
running it, so the printed line gives the same result without the launcher. No task hides a flag that
changes the meaning of a build. `dotnet run Tools/Tasks.cs help` lists them with their options, and `help <task>` shows the lines of one task
(`dotnet run` claims a `--help` placed after the file name, so write `-- <task> --help` to ask the same).

| Task | Runs |
|---|---|
| <!--#if (anyWeb)--> |
| `bootstrap` | `presentation-scaffold`, format, `docs-index`, write the OpenAPI reference, pin the project's own tool `dotnet cleanarch` at the template's version, restore the local tools, `migration-initial`, `api-client` where the project holds the .NET client, then `web-scaffold`; once, after generation |
| <!--#endif--> |
| <!--#if (!anyWeb && api)--> |
| `bootstrap` | `presentation-scaffold`, format, `docs-index`, write the OpenAPI reference, pin the project's own tool `dotnet cleanarch` at the template's version, restore the local tools, `migration-initial`, then `api-client` where the project holds the .NET client; once, after generation |
| <!--#endif--> |
| <!--#if (!api)--> |
| `bootstrap` | `presentation-scaffold`, format, `docs-index`, pin the project's own tool `dotnet cleanarch` at the template's version, restore the local tools, then `migration-initial`; once, after generation |
| <!--#endif--> |
| `check` | what `verify` would catch on the files you touched, in under a minute once built: [below](#check) |
| `restore`, `build`, `test`, `format-fix` | the `dotnet` command of the same name over the solution; `test --no-integration` builds every test project, runs only the ones that need no Docker, in parallel as over the solution, through solution filters it writes under the ignored artifacts folder, and names each project it built and did not run; off Windows, `test` also names and leaves out [the tests that run on Windows alone](#tests-that-run-on-windows-alone); `format-fix` then names each file and line whose name breaks a naming rule, which `dotnet format` cannot rename, and fails while one is left |
| `format` | `dotnet format --verify-no-changes` over what the build does not check, as [the gate](#the-gate) says |
| `coverage` | the tests with coverage, against `coverage.minimum` |
| <!--#if (api)--> |
| `run` | the API on the host |
| <!--#endif--> |
| `compose-up`, `compose-down` | the local stack, waiting until every service is healthy |
| <!--#if (api)--> |
| `end-to-end` | the end-to-end suite against a Compose stack of its own, then with a web app the browser smoke over each app, the stack removed afterwards unless `--keep-stack` |
| <!--#endif--> |
| `new-feature`, `remove-feature` | the feature generator and its undo |
| `new-field` | one or more fields added to a feature `new-feature --fields` generated, in every layer, with its migration |
| `new-use-case` | one more use case in a feature of the rich shape |
| `new-setting` | a feature's options class with its validator, registered, its section in each host's `appsettings.json` and in the configuration reference, as [adding a setting](adding-a-setting.md) says |
| `new-event` | a domain event of a feature of the rich shape, with its consumer or its place among the events nobody consumes, and their tests, as [adding a domain event](adding-a-feature.md#add-a-domain-event) says |
| `migration-initial` | each context's initial migration, where its `Migrations` folder holds none |
| <!--#if (localAuth)--> |
| `migration-add`, `database-update` | `dotnet ef` over the business context, or the identity context with `--context auth`, once it has its initial migration; `migration-add` refuses an empty migration |
| `migration-check` | fail when the business context's model, or the identity context's with `--context auth`, holds a change no migration holds; `new-feature` and `new-field` run it before they write |
| <!--#endif--> |
| <!--#if (!localAuth)--> |
| `migration-add`, `database-update` | `dotnet ef` over the business context, once it has its initial migration; `migration-add` refuses an empty migration |
| `migration-check` | fail when the business context's model holds a change no migration holds; `new-feature` and `new-field` run it before they write |
| <!--#endif--> |
| `migration-bundle` | one self-contained migration bundle per context, under `artifacts/migrate` |
| <!--#if (api)--> |
| `openapi` | rewrite the reviewed OpenAPI reference the contract test compares the served document with |
| `api-client` | once the project holds the .NET client, rewrite its `Generated/` folder from the OpenAPI reference, as `bootstrap` does after `migration-initial`; `--check` fails on a difference |
| <!--#endif--> |
| <!--#if (sonar)--> |
| `sonar` | the local Sonar analysis |
| <!--#endif--> |
| <!--#if (anyWeb)--> |
| `web-scaffold` | create each web app still holding its scaffold.json |
| <!--#endif--> |
| <!--#if (!anyWeb)--> |
| `web-scaffold` | create the framework app a first `cleanarch-webapp --workspace` adds |
| <!--#endif--> |
| `presentation-scaffold` | create each presentation project still holding its scaffold.json |
| `presentation-join` | the lines a presentation project added by `cleanarch-presentation` lists in its `README.md`, outside its folders, then records the project in `cleanarch.json`; for the worker it first moves the API's jobs and their tests into it, on a committed tree, and builds the result in a copy before it writes anything |
| `web-join` | for an app added by `cleanarch-webapp`, the lines its `README.md` lists outside its folders, with those the web workspace's own README lists for a first app and its workspace, then records the app in `cleanarch.json` with `--web`, `--web-audience` and `--web-rendering`, and the port its `Src/Web/apps/<App>/compose.yml` holds, which `--web-port` may state; for a package added by `cleanarch-webpackage`, records it |
| `docs-index` | the index of each folder under `docs/`, listing its pages and its subfolders by the `title` and `description` of their frontmatter, a decision with its `status`; `--check` fails when one differs from what it writes |
| `hygiene` | the self-tests of the gates this tree carries, then `docs-index --check` |
| `verify` | the whole gate, in CI's order |
| `upgrade-report` | what a newer version of the package that generated the project changes in it, written nowhere in the project, as [upgrading](upgrading.md) says |

`build`, `test`, `check`, `coverage`, `migration-bundle` and `verify` build in Debug unless
`dotnet run Tools/Tasks.cs -- <task> --configuration Release` asks otherwise: the `--` comes first,
because `dotnet run` claims `--configuration` for itself.

Everything under `Tools/` is a file-based C# app: the SDK `global.json` requires is all it needs. The
first run of a file compiles it, which takes a moment, under the same `Directory.Build.props` and
`.editorconfig` as the solution.

## Check

While you work, `dotnet run Tools/Tasks.cs check` runs the part of the gate a change of a few files can
break, on the files `git diff` touches: the working tree, the files not yet tracked and the commits of
the branch since its merge-base with `origin/develop`, or with the first of `origin/HEAD`,
`origin/main`, `origin/master`, `develop`, `main` and `master` that exists, or with `HEAD` when none
does. With nothing touched it says so and passes. In this order it:

1. verifies the format of the C# files touched, as `format` does for the whole tree;
2. builds the projects touched and every project that references them, found through the
   `ProjectReference` graph of the solution, or the whole solution when the change touches a file that
   sets the build for all of them: a project or solution file, `Directory.Build.props`,
   `Directory.Build.targets`, `Directory.Packages.props`, `.editorconfig`, `global.json` or
   `nuget.config`;
3. builds every test project that needs Docker, touched or not, so that a compile error there fails
   the task, and names each one as built and not run;
4. runs the architecture tests, whatever was touched, and the unit tests of the projects built, which
   are the projects that need no Docker and, off Windows, not
   [the ones that run on Windows alone](#tests-that-run-on-windows-alone).

The build fails on an `IDE1006` naming rule like any warning. `check` stands in for `verify` on a
small change and for nothing else: it builds the test projects that need Docker and runs none of
them, and leaves out `coverage`, the migration drift check, the vulnerability listing, the `hygiene` gates and, where the
project has them, the comparison of the generated clients with the OpenAPI reference. Run
`verify` and `coverage` before calling a change done.
<!--#if (templateMaintenance)-->
It also leaves out the template's own gates.
<!--#endif-->

## Tests that run on Windows alone

A test project whose own project file sets a Windows target framework, such as
`<TargetFramework>net10.0-windows</TargetFramework>`, runs on Windows alone. Off Windows, `test`,
`check`, `coverage` and `verify` build it with the rest and leave its tests out, through a solution
filter written under the ignored artifacts folder, and name it before the run and again after it:

```text
Skipped Tests/Presentation/<Folder>: it targets net10.0-windows and runs on Windows alone.
```

The run then passes or fails on the other projects; on Windows nothing is left out. The tasks read
the target framework in the test project's own file alone, so `WindowsTestProjectTests` fails when a
test project that targets Windows, or references a project that does, does not set it there, names
several target frameworks, or takes a Windows one from a `.props` or `.targets` file.

## The gate

A change is done when this passes, without overrides:

```bash
dotnet run Tools/Tasks.cs verify
```

It refuses to start while a context has no migration, then runs `hygiene`, restore, `format`, the
build, the tests, the migration drift check for every context, and the vulnerability listing. The
coverage floor is a separate task, `coverage`.
<!--#if (templateMaintenance)-->
In this repository the acceptance matrix also runs `coverage` in a generated project unlike this
checkout, against the same `coverage.minimum`, as
[maintaining the template](../template/maintaining-the-template.md#the-acceptance-matrix) says.
<!--#endif-->
When the project holds the .NET client, it runs `api-client --check` after `hygiene`, so a contract
change without its regenerated client fails.
<!--#if (anyWeb)-->
With a web workspace it then runs `pnpm install --frozen-lockfile` and compares
`Src/Web/packages/api-client/src/generated/schema.ts` with what `openapi-typescript` writes from the
OpenAPI reference, so a contract change without `pnpm api` fails and the message names that command,
then `pnpm audit`, and, once `cleanarch.json` records the workspace, `pnpm lint`, `pnpm test` and
`pnpm build` as the web workflow does. Those steps need Node.js and pnpm: without pnpm, `verify`
stops before them and says how to install it, and no option skips them.
<!--#endif-->

On a machine short of memory, `-m:<N>` caps `restore`, `build`, `test`, `coverage` and `verify` at
N MSBuild nodes and N test projects running at once, as in `dotnet run Tools/Tasks.cs verify -m:4`.

Each gate proves it can fail before it judges the tree: `hygiene` runs the `--self-test` of the
generator, the removal command and the coverage gate, each against faulted and sound fixtures, then
fails when a folder's index under `docs/` no longer lists its pages as their frontmatter says. A
gate also refuses to pass on nothing: a test run that executed no test and a coverage total over no
assembly both fail. A rule added to a gate comes with the fixture that fails without it.

`TreatWarningsAsErrors`, set in `Directory.Build.props`, fails the build on a compiler's warning, and
`MSBuildTreatWarningsAsErrors`, set in `Directory.Build.targets`, on a warning a build task logs, such
as the Avalonia XAML compiler's. The second is left out of SonarScanner's analysis build, which turns
the first off itself: Sonar reports its findings as warnings for its quality gate to judge, and the
targets file is read after the scanner's own, which define `SonarQubeTempPath`. A diagnostic is
silenced only by a row of the table below, whatever the form: a `#pragma warning disable`, a
`[SuppressMessage]`, a `#nullable disable`, a `<NoWarn>` or a `<WarningsNotAsErrors>`, a severity
lowered to `none`, `silent` or `suggestion`, a `generated_code` section, a test's `Skip`, or a build
told not to fail on its warnings. Anything else is fixed in the code. A diagnostic wrong for one file
gets a section of `.editorconfig` naming that file, and its row here. `SilencedDiagnosticTests` finds
each one in the tree and fails on one without its row, on a row that silences nothing, and on a
section that names a file the tree does not hold. Inside a `generated_code` section, the code is the
tool's and keeps the directives the tool writes.

| Diagnostic | Where | Why |
|---|---|---|
| <!--#if (api)--> |
| `CS1591`, `CS1573` | `Directory.Build.props` | The documentation file is generated for the build to report IDE0005 and for the OpenAPI document to read its summaries, not to make every public member of an application carry a comment, which the comment rule would then have restate a name. |
| <!--#endif--> |
| <!--#if (!api)--> |
| `CS1591`, `CS1573` | `Directory.Build.props` | The documentation file is generated for the build to report IDE0005, not to make every public member of an application carry a comment, which the comment rule would then have restate a name. |
| <!--#endif--> |
| `CA1848` | `.editorconfig` `*.cs` | A log call stays one sentence at the line it describes; the `[LoggerMessage]` generator would declare each one apart, as a partial method, to save an allocation no path here has measured as worth it. |
| <!--#if (api)--> |
| `AV0029`, `AV0030` | `Src/Presentation/AT.Api/AppTemplate.Api.csproj` | Both ask for `Asp.Versioning.OpenApi`, whose transformer renders a `<see cref>` in a schema description as an empty string and titles the document after the entry assembly; `OpenApiDocumentTests` asserts on both. Project-wide, since AV0029 also fires in the source the OpenAPI generator writes into the build. |
| <!--#endif--> |
| `CA1716` | `.editorconfig` `Src/Application/AT.Application.Core/Common/Results/Error.cs` | A C#-only API, and `Error` is the Result pattern's name for the concept; the rule guards a Visual Basic keyword. |
| `CA1000` | `.editorconfig` `Src/Application/AT.Application.Core/Common/Results/Result.cs` | `Result<TValue>.Failure` implements the static abstract `IFailable<TSelf>.Failure` the pipeline behaviours call, and the type-inferring `Result.Success` and `Result.Failure` serve every other caller. |
| `S4136` | `.editorconfig` `Src/Application/AT.Application.Core/Common/Results/Result.cs` | The non-generic and generic overloads are paired, each beside its counterpart. |
| <!--#if (reminders)--> |
| `S107` | `.editorconfig` `Src/Application/AT.Application/Features/Reminders/UseCases/Commands/FireDueReminders/FireDueReminders.cs` | Nine distinct collaborators, each needed once; a parameter object would only rename the constructor. |
| <!--#endif--> |
| `S4035` | `.editorconfig` `Src/Domain/AT.Domain.Core/Common/Primitives/Entity.cs` | The abstract base of every aggregate, whose `Equals` compares the runtime type first. |
| <!--#if (files)--> |
| `S3267` | `.editorconfig` `Src/Domain/AT.Domain/Features/Files/Model/ValueObjects/Sha256Checksum.cs` | The loop refuses on the first bad character; a `Where` would collect them all first. |
| `S3267` | `.editorconfig` `Src/Domain/AT.Domain/Features/Files/Model/ValueObjects/StoredFileName.cs` | The loop refuses on the first control character; a `Where` would collect them all first. |
| <!--#endif--> |
| <!--#if (localAuth)--> |
| `S6672` | `.editorconfig` `Src/Infrastructure/AT.Infrastructure.Auth/Features/Accounts/Adapters/Providers/EmailChangeTokenProvider.cs` | The logger's type argument is fixed by `DataProtectorTokenProvider<AppUser>`, the base class. |
| `S6672` | `.editorconfig` `Src/Infrastructure/AT.Infrastructure.Auth/Features/Accounts/Adapters/Providers/PasswordResetTokenProvider.cs` | The logger's type argument is fixed by `DataProtectorTokenProvider<AppUser>`, the base class. |
| <!--#endif--> |
| `S2068` | `.editorconfig` `Src/Infrastructure/AT.Infrastructure.Core/Common/Contexts/DefaultConnectionString.cs` | The local Compose database's design-time fallback, not a secret. |
| `S2326` | `.editorconfig` `Src/Infrastructure/AT.Infrastructure.Core/Common/Saving/IContextUnitOfWork.cs` | `TContext` is a marker that makes one closed generic per context. |
| `S3267` | `.editorconfig` `Src/Infrastructure/AT.Infrastructure.Core/Common/Saving/Tracking/AggregateTracker.cs` | The loop drains and clears each aggregate's events; a projection would do neither. |
| <!--#if (files)--> |
| `S3267` | `.editorconfig` `Src/Infrastructure/AT.Infrastructure.Persistence/Features/Files/Rows/Mapping/StoredFileMapper.cs` | A reconciliation with two side effects, not a filter. |
| <!--#endif--> |
| <!--#if (todoLists)--> |
| `S3267` | `.editorconfig` `Src/Infrastructure/AT.Infrastructure.Persistence/Features/TodoLists/Rows/Mapping/TodoListMapper.cs` | A reconciliation with two side effects, not a filter. |
| <!--#endif--> |
| <!--#if (api)--> |
| `S2139` | `.editorconfig` `Src/Presentation/AT.Api/Common/Hosting/DevelopmentDatabaseExtensions.cs` | The development database bootstrap logs critically once, then fails the start. |
| `S4136` | `.editorconfig` `Src/Presentation/AT.Api/Common/Controllers/ApiControllerBase.cs` | The private helper sits under its only caller, between the overloads. |
| `S1075` | `.editorconfig` `Src/Presentation/AT.Api/Common/Errors/ProblemTypes.cs` | The default problem-type base, on the reserved `.example` domain; `ProblemTypeOptions.BaseUri` replaces it. |
| <!--#endif--> |
| `generated_code`, `IDE0005` | `.editorconfig` `**/Migrations/*.cs` | `dotnet ef` writes the migrations, and one a database has applied is never rewritten. |
| `generated_code` | `.editorconfig` `**/Generated/**.cs` | Kiota writes the .NET client's `Generated/` folder, and `api-client` rewrites it whole. |
| `CA1707` | `.editorconfig` `Tests/**/*.cs` | A test's name reads as its claim, its parts separated by underscores. |
| <!--#if (templateMaintenance)--> |
| `CA1707` | `Launcher/.editorconfig` `CleanArch.Create.Tests/**.cs` | A test's name reads as its claim, its parts separated by underscores. |
| `CA1707` | `Launcher/.editorconfig` `CleanArch.Tooling.Tests/**.cs` | A test's name reads as its claim, its parts separated by underscores. |
| `CA1707` | `Launcher/.editorconfig` `CleanArch.Cli.Tests/**.cs` | A test's name reads as its claim, its parts separated by underscores. |
<!--#endif-->

The build holds the code style: `EnforceCodeStyleInBuild` runs the `.editorconfig` rules during
compilation, so a formatting defect (IDE0055), a private field without its underscore (IDE1006) or an
unused `using` (IDE0005) fails `dotnet build` like any warning. IDE0055 and IDE1006 need their
`dotnet_diagnostic` severity in `.editorconfig`, and IDE0005 needs the documentation file every
project generates; `CodeStyleEnforcementTests` fails when either is missing. What the build cannot
see is left to `format`: the order of using directives, the encoding and line endings, and IDE0003
and IDE0049, whose analyzers the SDK's build does not carry. `verify` runs it before the build.

`.cs` files are UTF-8 with BOM: one created without it fails `format`, and `format-fix` adds it.
`.md` files are UTF-8 without BOM, with LF line endings: `MarkdownEncodingTests` fails on a BOM or a
CR, and `format-fix` removes both.
<!--#if (ci)-->

## Workflows

| Workflow | When | Does |
|---|---|---|
| <!--#endif--> |
| <!--#if (ci && api)--> |
| `.github/workflows/ci.yml` | push and pull request to `main` or `develop`, and a manual run | restore, formatting, build, every test project in the solution, tests with coverage and the floor, and the Sonar analysis around them when it is set up; the `hygiene` gates; the vulnerability listing; the API image, unprivileged and exposing 8080 only; the Compose manifest; the end-to-end suite against the stack, then the browser smoke with a web app; the load smoke |
| <!--#endif--> |
| <!--#if (ci && !api)--> |
| `.github/workflows/ci.yml` | push and pull request to `main` or `develop`, and a manual run | restore, formatting, build, every test project in the solution, tests with coverage and the floor, and the Sonar analysis around them when it is set up; the `hygiene` gates; the vulnerability listing; the Compose manifest |
| <!--#endif--> |
| <!--#if (ci && serverDatabase && api)--> |
| `.github/workflows/release.yml` | a `v*.*.*` tag | the gate again, the migration bundles, and the API's image, with the worker's when the project holds one, pushed to GHCR |
| <!--#endif--> |
| <!--#if (ci && serverDatabase && !api)--> |
| `.github/workflows/release.yml` | a `v*.*.*` tag | the gate again, the migration bundles, and the worker's image pushed to GHCR |
| <!--#endif--> |
| <!--#if (ci && !anyWeb)--> |
| `.github/workflows/codeql.yml` | push and pull request to `main` or `develop`, and weekly | CodeQL code scanning of the C# sources, read without a build; alerts land in the repository's Security tab |
| <!--#endif--> |
| <!--#if (ci && anyWeb)--> |
| `.github/workflows/codeql.yml` | push and pull request to `main` or `develop`, and weekly | CodeQL code scanning of the C# sources, read without a build, and of the TypeScript in `Src/Web`; alerts land in the repository's Security tab |
| <!--#endif--> |
| <!--#if (ci && anyWeb)--> |
| `.github/workflows/web.yml` | push to `main`, pull request to `main` or `develop`, and a manual run | in `Src/Web`: frozen install, lint, test, build, and a failure when the API client differs from the API's document; one image per app |
<!--#endif-->
<!--#if (ci)-->

CodeQL reads the C# without building it, so it neither repeats the build `ci.yml` runs nor sees what
a build generates: the output of a source generator is not analysed, the committed Kiota client and
the migrations are.

<!--#endif-->
<!--#if (ci && api)-->
The load smoke never gates: its thresholds describe a broken system, and a `429` counts as a pass,
because a hosted runner's timings say nothing about this code. Run it against your own stack with
`k6 run -e BASE_URL=http://localhost:8080 Tests/Load/smoke.js`.

<!--#endif-->
<!--#if (ci)-->
Every action is pinned to a commit SHA and every image to a digest, `permissions` is
`contents: read` unless a job needs more, and a newer run of `ci.yml` or `codeql.yml` on a branch other
than `main` and `develop` cancels the older one.
<!--#endif-->
<!--#if (ci && anyWeb)-->
A newer run of `web.yml` cancels the older one on every branch but `main`.
<!--#endif-->
<!--#if (ci)-->
`WorkflowPinningTests` reads every file under `.github/workflows` and fails on an action named by
anything but its 40-character commit SHA, a local `./` action excepted, and on an image a workflow
runs, as a `container:`, a service, a `docker://` step or the BuildKit driver, without its digest.
<!--#endif-->
<!--#if (sonar)-->

## Sonar

Sonar is not one of the gates: it needs a server and a token, which a clone does not have.
<!--#endif-->
<!--#if (sonarWorkflow)-->
In CI it can still fail a pull request: the quality gate judges new code only, so the code already
there blocks nobody. It runs inside the `Build & test` job of `.github/workflows/ci.yml`, around the
build and the tests that job already runs, so the code is not built a second time. The job waits on
the quality gate and fails when it is red or when SonarCloud cannot be reached, which makes the gate
a required check wherever `Build & test` is one. The steps are skipped until the repository variables
`SONAR_ORGANIZATION` and `SONAR_PROJECT_KEY` are set, and for a fork's or Dependabot's pull request;
they read the secret `SONAR_TOKEN`. The first analysis of the default branch can come back red,
since everything is new code until a baseline exists.
<!--#endif-->
<!--#if (sonar)-->

Locally:

```bash
docker compose --profile sonar up -d --wait sonarqube   # http://localhost:9111, admin/admin
dotnet run Tools/Tasks.cs sonar                         # after setting SONAR_TOKEN in .env
```

The scanner needs a JRE, which `Tools/sonar-scanner.Dockerfile` supplies, so your machine needs none.
It builds into `artifacts/sonar`, leaving your `bin/` and `obj/` alone. It runs the Docker-free tests
only, so it reports less coverage than CI, and, the scanner running on Linux, names and leaves out
[the ones that run on Windows alone](#tests-that-run-on-windows-alone).
<!--#endif-->
<!--#if (ci && serverDatabase)-->

## Release

<!--#endif-->
<!--#if (ci && serverDatabase && api)-->
Push a tag such as `v1.2.3`. The release re-runs the gate, then publishes the API's image to GHCR, and
the worker's when the project holds one, each as
`1.2.3`, `1.2`, `1` and `latest` (a prerelease tag never moves `latest`), multi-arch, with an SBOM and
a signed provenance attestation, and uploads the migration bundles built from the same commit. It
needs no secret beyond `GITHUB_TOKEN`. Applying the bundles is yours:
[deployment](deployment.md#migrations-at-release).
<!--#endif-->
<!--#if (ci && serverDatabase && !api)-->
Push a tag such as `v1.2.3`. The release re-runs the gate, then publishes the worker's image to GHCR as
`1.2.3`, `1.2`, `1` and `latest` (a prerelease tag never moves `latest`), multi-arch, with an SBOM and
a signed provenance attestation, and uploads the migration bundles built from the same commit. It
needs no secret beyond `GITHUB_TOKEN`. Applying the bundles is yours:
[deployment](deployment.md#migrations-at-release).
<!--#endif-->

## Supply chain

Every dependency but the SDK is at an exact version, and `.github/dependabot.yml` proposes updates
weekly, in bands; nothing is ignored. The SDK is a floor: `global.json` names the lowest version and
lets a later feature band of the same major version run, and each Dockerfile pins the SDK image it
builds with.

| Dependency | Pinned in |
|---|---|
| NuGet packages | `Directory.Packages.props` |
| The SDK, as a floor | `global.json` |
| The .NET tools | `.config/dotnet-tools.json` |
| Container images and actions | their digest or commit SHA, beside the tag |
| <!--#if (anyWeb)--> |
| Node, pnpm and the web packages | `Src/Web/.node-version`, `Src/Web/package.json`, `Src/Web/pnpm-lock.yaml` |
<!--#endif-->

### An advisory fails the build

`NuGetAudit` fails the build on any known advisory, `low` included, on a direct package as on a
transitive one: `Directory.Build.props` sets `NuGetAuditMode` to `all` and `NuGetAuditLevel` to
`low`, under `TreatWarningsAsErrors`. A project that is born vulnerable does not build in silence
([ADR 0030](../decisions/0030-any-known-advisory-fails-the-build.md)), so a build can fail the day an
advisory is published, with no change to the code.

When the build stops on an `NU1901`, `NU1902`, `NU1903` or `NU1904`:

1. Read the warning: it names the package, its version and the advisory's address.
2. Raise the package in `Directory.Packages.props`. Most advisories arrive through a package nobody
   references directly; the answer is then a transitive pin there, with a guidance line saying
   when it can go. Stay on the major the referencing package was built against.
3. When no fixed version exists or can be taken yet, add `<NuGetAuditSuppress Include="<advisory URL>" />`
   to `Directory.Build.props` for that one advisory, with a guidance line giving the date and the
   reason, and remove it when a fixed version ships.

Never lower the level, switch the audit to `direct`, or put the code in a `<NoWarn>`:
`NuGetAuditPolicyTests` fails on each. `dotnet list package --vulnerable --include-transitive` exits
zero whatever it finds, so `verify` and CI read what it prints.
<!--#if (anyWeb)-->

`pnpm audit` holds the web packages to the same rule: `verify` and the web workflow run it, and any
advisory fails, whatever its severity. The answer is the same too: raise the dependency, or pin a
patched release under `overrides` in `Src/Web/pnpm-workspace.yaml` with a comment saying when it can go;
a package that cannot take the pinned release as it is gets a pnpm patch in `Src/Web/patches/`.
<!--#endif-->

<!--#if (templateMaintenance)-->
The template builds what nuget.org serves each week, so an advisory published since the last release
shows before a user meets it:
[the weekly build of the published package](../template/maintaining-the-template.md#the-published-package-each-week).

<!--#endif-->
## Decisions

The toolchain, the pinning and Dependabot are rows of
[ADR 0001](../decisions/0001-adopt-the-generated-baseline.md).
