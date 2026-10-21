# Build, gates and CI

How this repository is built and how it judges itself. Everything below runs from a clone — no
account, no token, no third party — with one deliberate exception, named at the end.

- [A green build](#a-green-build)
- [The gate](#the-gate)
- [`TreatWarningsAsErrors` is not negotiable](#treatwarningsaserrors-is-not-negotiable)
- [`.cs` files must be UTF-8 with BOM](#cs-files-must-be-utf-8-with-bom)
- [`Tools/`](#tools)
  <!--#if (ci)-->
- [CI](#ci)
  <!--#endif-->
- [Supply chain](#supply-chain)
- [The container image](#the-container-image)
  <!--#if (sonarWorkflow)-->
- [Static analysis, and where its verdict lives](#static-analysis-and-where-its-verdict-lives)
  <!--#endif-->

## A green build

```bash
dotnet restore AppTemplate.sln
dotnet build   AppTemplate.sln
dotnet test    AppTemplate.sln          # needs Docker: see TESTING.md
```

Or through the task wrappers, which print the real command before running it, so the script is also
the documentation:

```bash
dotnet run Tools/Tasks.cs restore
dotnet run Tools/Tasks.cs build
dotnet run Tools/Tasks.cs test
dotnet run Tools/Tasks.cs hygiene                # every gate this tree carries, fixtures first
dotnet run Tools/Tasks.cs verify                 # the whole gate, in CI's order
```

Prerequisites are the .NET SDK version pinned in `global.json` — nothing else, because everything
under `Tools/` is a file-based C# app — and Docker for the integration tests. `hygiene` costs the
SDK like every other task here, which is the deliberate trade: the machine that can build this
repository can also judge it, straight after a clone.

The full task list is in [`GETTING-STARTED.md`](GETTING-STARTED.md#the-task-launcher).

## The gate

A change is done when all of these pass. `dotnet run Tools/Tasks.cs verify` runs them in this
order — formatting first, because it is the fastest to fail.

```bash
dotnet run Tools/Tasks.cs hygiene                  # every gate this tree carries, fixtures first
dotnet restore AppTemplate.sln
dotnet format AppTemplate.sln --verify-no-changes  # clean
dotnet build  AppTemplate.sln                      # 0 warnings, 0 errors
dotnet test   AppTemplate.sln                      # all green
dotnet ef migrations has-pending-model-changes     # both contexts — see MIGRATIONS.md
dotnet list package --vulnerable --include-transitive   # reports nothing
dotnet run Tools/Tasks.cs coverage                 # line coverage >= coverage.minimum
```

<!--#if (templateMaintenance)-->
`hygiene` exists because three classes of defect are invisible to the compiler and to the tests: a
documented path that resolves to nothing, a workflow that would fail the first time it ran, and a
comment the code should not need.
<!--#else-->
`hygiene` runs the fixtures of the gates this tree carries — the scaffolders' and the coverage
gate's — so a gate that cannot go red never judges the tree.
<!--#endif-->

### A gate proves it can go red before it judges anything

<!--#if (templateMaintenance)-->
Each of the four hygiene gates takes a `--self-test` flag, and running it is a step in its own
right rather than a nicety:

```bash
dotnet run Tools/CheckDocPaths.cs --self-test
dotnet run Tools/CheckWorkflows.cs --self-test
dotnet run Tools/CoverageGate.cs --self-test
dotnet run Tools/CheckComments.cs --self-test
```

Each carries two sets of fixtures, built in a temporary tree and thrown away. The faulted ones —
each labelled with the defect it stands for — **must** make the gate fire, and the sound ones
**must** leave it silent, which is the half that catches a rule grown eager enough to flag a
legitimate line. A gate whose green has never been contrasted with a red says nothing about the
repository; it says only that it ran.

`hygiene` and `verify` run every self-test before letting the corresponding gate look at the tree,
and CI does the same, one step per self-test so a fixture failure is legible on its own rather than
buried in the gate that follows it. **Adding a rule to a gate means adding the fixture that fails
without it.**
<!--#else
Each gate the tree carries takes a `--self-test` flag, and `hygiene` runs every one of them before
letting a gate look at the tree: a gate whose green has never been contrasted with a red says
nothing about the repository. **Adding a rule to a gate means adding the fixture that fails
without it.**
#endif-->

## `TreatWarningsAsErrors` is not negotiable

It is set in `Directory.Build.props` and applies to every project. Do not override it from the
command line, and do not add `NoWarn`.

**You may not make the build pass by silencing the compiler.** No `#pragma warning disable`, no
`<NoWarn>`, no lowering a severity in `.editorconfig`. If a diagnostic is genuinely wrong for one
specific place, add a **path-scoped** `.editorconfig` section for that file with a comment saying
why. Every existing suppression follows that rule and you can read the reasoning next to it.

Those suppressions are **indexed by path**: move a file and its suppression silently stops
applying, and the build breaks. That is intended — it forces the exemption to be re-justified at
its new home.

## `.cs` files must be UTF-8 with BOM

`.editorconfig` sets `charset = utf-8-bom`, for `.md` as well as `.cs`, with no exception. A file
created without the BOM fails `dotnet format --verify-no-changes` on encoding alone, with a message
that does not obviously say so. After creating or moving any file:

```bash
dotnet run Tools/Tasks.cs format-fix     # i.e. dotnet format AppTemplate.sln
```

Detecting a BOM by piping bytes through `grep` does not work reliably — it will happily add a
second one.

## `Tools/`

<!--#if (templateMaintenance)-->
Eight single-file C# apps, each launched with `dotnet run <file>`, each needing nothing beyond the
SDK `global.json` pins — no interpreter to find, no package to install, and the same line typed on
Windows, Linux and macOS. The ninth entry is the one exception, and it is a Dockerfile rather than
an app.

| File | What it is |
|---|---|
| [`Tools/Tasks.cs`](../Tools/Tasks.cs) | the task launcher: thin wrappers over the real `dotnet` and `docker` commands |
| [`Tools/NewFeature.cs`](../Tools/NewFeature.cs) | the feature generator — the skeleton of one feature in the shape, operations and concurrency chosen, plus a creation manifest, so `ADDING-A-FEATURE.md` can be about the thinking. Its `--self-test` runs the real command against throwaway trees |
| [`Tools/RemoveFeature.cs`](../Tools/RemoveFeature.cs) | the report of what removing a feature involves, and with `--apply` the undo of a generated skeleton whose manifest hashes, folders and surroundings are all untouched. Its `--self-test` generates features with the real generator and removes them |
| [`Tools/CheckDocPaths.cs`](../Tools/CheckDocPaths.cs) | every repository path cited in a Markdown code span resolves on disk |
| [`Tools/CheckWorkflows.cs`](../Tools/CheckWorkflows.cs) | what a YAML parse does not catch: a dangling `needs:`, an action on a mutable tag, a container image without a digest, a missing `permissions:`, a `$VAR` in no `env:`, a script named in a `run:`, a working directory or an action's path input absent from disk |
| [`Tools/CoverageGate.cs`](../Tools/CoverageGate.cs) | the Cobertura reports of a test run against the floor in `coverage.minimum` |
| [`Tools/CheckComments.cs`](../Tools/CheckComments.cs) | no comment outside the allowlist of [`CONVENTIONS.md`](CONVENTIONS.md#comments): C#, project files, the guidance lines of the configuration files, and no Markdown narrating the repository's history |
| [`Tools/TestSummary.cs`](../Tools/TestSummary.cs) | sums the TRX counters into the job summary, and fails a run that executed no test |
| [`Tools/sonar-scanner.Dockerfile`](../Tools/sonar-scanner.Dockerfile) | not a C# app: the image the local Sonar analysis runs in, which is where its JRE lives instead of on your machine |
<!--#elseif (sonar)
Five single-file C# apps, each launched with `dotnet run <file>`, each needing nothing beyond the
SDK `global.json` pins — no interpreter to find, no package to install, and the same line typed on
Windows, Linux and macOS. The sixth entry is the one exception, and it is a Dockerfile rather than
an app.

| File | What it is |
|---|---|
| [`Tools/Tasks.cs`](../Tools/Tasks.cs) | the task launcher: thin wrappers over the real `dotnet` and `docker` commands |
| [`Tools/NewFeature.cs`](../Tools/NewFeature.cs) | the feature generator — the skeleton of one feature in the shape, operations and concurrency chosen, plus a creation manifest, so `ADDING-A-FEATURE.md` can be about the thinking. Its `--self-test` runs the real command against throwaway trees |
| [`Tools/RemoveFeature.cs`](../Tools/RemoveFeature.cs) | the report of what removing a feature involves, and with `--apply` the undo of a generated skeleton whose manifest hashes, folders and surroundings are all untouched. Its `--self-test` generates features with the real generator and removes them |
| [`Tools/CoverageGate.cs`](../Tools/CoverageGate.cs) | the Cobertura reports of a test run against the floor in `coverage.minimum` |
| [`Tools/TestSummary.cs`](../Tools/TestSummary.cs) | sums the TRX counters into the job summary, and fails a run that executed no test |
| [`Tools/sonar-scanner.Dockerfile`](../Tools/sonar-scanner.Dockerfile) | not a C# app: the image the local Sonar analysis runs in, which is where its JRE lives instead of on your machine |
#else
Five single-file C# apps, each launched with `dotnet run <file>`, each needing nothing beyond the
SDK `global.json` pins — no interpreter to find, no package to install, and the same line typed on
Windows, Linux and macOS.

| File | What it is |
|---|---|
| [`Tools/Tasks.cs`](../Tools/Tasks.cs) | the task launcher: thin wrappers over the real `dotnet` and `docker` commands |
| [`Tools/NewFeature.cs`](../Tools/NewFeature.cs) | the feature generator — the skeleton of one feature in the shape, operations and concurrency chosen, plus a creation manifest, so `ADDING-A-FEATURE.md` can be about the thinking. Its `--self-test` runs the real command against throwaway trees |
| [`Tools/RemoveFeature.cs`](../Tools/RemoveFeature.cs) | the report of what removing a feature involves, and with `--apply` the undo of a generated skeleton whose manifest hashes, folders and surroundings are all untouched. Its `--self-test` generates features with the real generator and removes them |
| [`Tools/CoverageGate.cs`](../Tools/CoverageGate.cs) | the Cobertura reports of a test run against the floor in `coverage.minimum` |
| [`Tools/TestSummary.cs`](../Tools/TestSummary.cs) | sums the TRX counters into the job summary, and fails a run that executed no test |
#endif-->

The tree-auditing gates — `CheckDocPaths`, `CheckWorkflows`, `CheckComments` — are the
template repository's own and do not ship: they hold this template's documentation, workflows and
comment policy to this template's tree. The `hygiene` task asks the filesystem which gates its
tree carries, so the same task serves both.

`Tools/Tasks.cs` prints each command before running it, which is what keeps the launcher honest:
copy the printed line and you get the same result without it. **No task hides a flag that changes
the meaning of a build** — in particular none relaxes `TreatWarningsAsErrors`.

Each app states its usage itself: `dotnet run Tools/<file> -- --help`, the separator keeping
`dotnet run` from answering with its own help, and `dotnet run Tools/Tasks.cs help` for the
launcher.

<!--#if (ci)-->
## CI

`.github/workflows/ci.yml`, on push and pull request to `main`:

| Job | Does |
|---|---|
| Build & test | restore, `dotnet format --verify-no-changes`, build with warnings as errors, assert every test project is in the solution, run the tests with coverage, enforce the floor in `coverage.minimum` |
<!--#if (templateMaintenance)-->
| Docs and workflows | the four hygiene gates, each preceded by its own self-test |
<!--#else
| Gates | the `hygiene` task: the fixtures of the gates the tree carries |
#endif-->
| Vulnerable packages | `dotnet list package --vulnerable --include-transitive`, failing on any hit |
| Docker image | builds the image, then asserts it does not run as root and exposes only 8080 |
<!--#if (templateMaintenance)-->
| Template acceptance (twelve legs) | install → generate under a different name with one option combination → assert the template's name leaked nowhere → bootstrap → build → Docker-free tests → no pending model change per generated context → the generated project's `hygiene` task, and on a web leg the web workspace, its images and its stack; see [below](#template-acceptance) |
| Template refuses unsupported options | `--auth none` alone and with `--examples notes` must exit non-zero and write no file |
<!--#endif-->
| Load smoke | the k6 script, **non-blocking** — see [`TESTING.md`](TESTING.md#the-load-smoke-test) |
| Compose manifest | `docker compose config` against `.env.example` |

<!--#if (anyWeb)-->
`.github/workflows/web.yml`, on the same events, gates the web workspace; it is a file of its own
so that `dotnet new cleanarch-webapp --workspace` can add it to a project generated without a web
app:

| Job | Does |
|---|---|
| Web workspace | in `Src/Web`: `pnpm install --frozen-lockfile`, lint, test and build every app and package, then `pnpm api` and a failure if the generated API client differs from the committed one |
| Web image | one leg per folder under `Src/Web/apps` holding a Dockerfile and a `package.json` — a react or angular app counts once `web-scaffold` has created it and it is committed: builds the image from `Src/Web`, then asserts it does not run as root and exposes only 8080 |

<!--#endif-->
<!--#if (templateMaintenance)-->
### Template acceptance

A template that generates a broken project is worse than a repository to clone, and every option is a
different generated project, so the job is a matrix with `fail-fast: false` — one red leg must not hide
another. The legs are the combinations that exercise a different shape, not the cross product:

| Leg | Options | Contexts compared |
|---|---|---|
| defaults | `--auth local --examples all --files true` | Persistence, Auth |
| auth none, smallest project | `--auth none --examples none --files false` | Persistence |
| external auth, notes | `--auth external --examples notes` | Persistence |
| local auth, no files, no ci, no sonar | `--auth local --examples none --files false --ci false --sonar false` | Persistence, Auth |
| external auth, all examples | `--auth external --examples all --files true` | Persistence |
| external auth, no examples, no files | `--auth external --examples none --files false` | Persistence |
| web placeholder, smallest project | `--web placeholder --auth none --examples none --files false` | Persistence |
| web react, local auth | `--web react` | Persistence, Auth |
| web angular, external auth, notes | `--web angular --auth external --examples notes` | Persistence |
| web react back office, smallest local project | `--web react --web-audience admin --examples none --files false` | Persistence, Auth |
| web react, then an angular back office | `--web react`, then `cleanarch-webapp --framework angular --audience admin` | Persistence, Auth |
| no web, then a react app and its workspace | none, then `cleanarch-webapp --framework react --workspace` | Persistence, Auth |

Each leg builds with `TreatWarningsAsErrors` (from `Directory.Build.props`, as everywhere), runs the
test projects that do not reference Testcontainers — integration tests stay with the repository's own
build — and runs `dotnet run Tools/Tasks.cs hygiene` in the generated project, which asks the
filesystem which gates its tree carries. Only `--auth local` generates the Auth context, so only those legs
compare its model. A leg without a web app asserts that its tree holds no web workspace and that no
workflow, Dependabot entry, Compose file or ignore file names one; the API side a web app needs is in
every project. The leg generated without a web app that then takes one runs that assertion first,
adds the app with `dotnet new cleanarch-webapp -n Site --workspace` and the include block and port
`Src/Web/README.md` gives, and from there is a web leg like the others.
A web leg first runs `dotnet run Tools/Tasks.cs web-lockfiles --check` in this checkout, which fails
when the lockfile a generated web shape receives differs from what the pinned pnpm writes for it.
The web leg also adds a second app with `dotnet new cleanarch-webapp -n BackOffice`, a placeholder
unless the leg names an Angular back office, and the two lines its README gives. It then runs
`dotnet run Tools/Tasks.cs web-scaffold`, bootstrap's web step, which creates each react or angular
app with its framework's official CLI at the version the app's `scaffold.json` pins, installs and
regenerates the client; runs the workspace's frozen install, lint, test and build; adds a package with
`dotnet new cleanarch-webpackage -n tokens` and runs the same gates again; commits the tree
and checks that a second `web-scaffold` changes nothing, the client included; starts
the whole stack with Compose until every service is healthy, and asks each app for its page, its
security headers, `/health` and an `/api/` answer that only the API gives; the external leg's stack
names an identity provider that does not exist, which the API needs to start and reads from only when
a token arrives. The separate refusal job covers the rule the other way round: `--auth none` needs
`--examples none` and `--files false`, enforced by hidden required parameters, and an unsupported
combination must fail before it writes anything.

The legs live in `.github/workflows/template-acceptance.yml`, a workflow a generated project does
not receive: `dotnet new install .` of a generated project is a sentence that cannot be true.
<!--#endif-->

All actions are pinned to a commit SHA, every container image to a digest (see
[`DECISIONS.md`](DECISIONS.md)), `permissions:` is `contents: read`, and a concurrency group
supersedes superseded runs. **No CI step passes a flag that would turn warnings back into
warnings:** `TreatWarningsAsErrors=true` lives in `Directory.Build.props` and must stay
authoritative.

Coverage is collected over every test project **except** `AppTemplate.Architecture.Tests`, and that
split is deliberate — the reason is in [`TESTING.md`](TESTING.md#coverage-and-the-architecture-tests).
Every test still runs exactly once. Do not merge those two steps back together.

`.github/workflows/release.yml` runs on a `v*.*.*` tag: it re-runs the gate, publishes a multi-arch
image for both hosts to GHCR with an SBOM and a signed provenance attestation, and uploads a
self-contained migration bundle built from the same commit. It needs no secret beyond the automatic
`GITHUB_TOKEN`. [`MIGRATIONS.md`](MIGRATIONS.md#applying-them) names what that bundle does and does
not cover.

<!--#endif-->
## Supply chain

`Directory.Build.props` sets `NuGetAudit` with `NuGetAuditMode=all` and `NuGetAuditLevel=low`, so a
package with **any** known advisory fails the build, not just a critical one.

Most real advisories arrive through a package nobody referenced directly, and the answer to one is a
**transitive pin**: a `PackageVersion` for a package the solution never names, which
`CentralPackageTransitivePinningEnabled` makes bind anyway. Such a pin goes in
`Directory.Packages.props` under a `Security pins` label, with a comment saying what it holds back
and what has to be true before it can go — and it stays out of Dependabot's `ignore` list, because
the release that finally makes a pin unnecessary is the release nobody would otherwise hear about.

Stay on the major the referencing package was compiled against; a pin that jumps a major trades an
advisory for a load error. `dotnet list package --vulnerable --include-transitive` is the check, and
CI runs it on every push.

## The container image

```bash
docker build -f Src/Presentation/AppTemplate.Api/Dockerfile -t app-template-api:local .
```

The build context is the **repository root**, not the project directory — `Directory.Build.props`,
`Directory.Build.targets`, `Directory.Packages.props` and `global.json` must be copied before
`dotnet restore` or Central Package Management fails.

- Multi-stage: an SDK image builds, an ASP.NET Alpine image runs. Both pinned to a patch version
  and a digest (`name:tag@sha256:…`), and so is the `# syntax=` frontend.
- Runs as **non-root** (`USER $APP_UID`). The Alpine variant does not set `User` itself, so that
  line is load-bearing.
- Publishes portable IL on the build machine's architecture, so one build serves every target. For
  multi-arch: `docker buildx build --platform linux/amd64,linux/arm64 -f Src/Presentation/AppTemplate.Api/Dockerfile .`
- `HEALTHCHECK` on `/health`.
- `.dockerignore` keeps `appsettings.Development.json`, `.git` (including `.git/config`, which can
  carry a token in a remote URL), `Tests/`, certificates and keys out of the image layers.

> The Dockerfile's `COPY` list names each project file individually. **Adding a project reference to
> `AppTemplate.Api` means adding it here too**, or restore inside the image fails on a missing
> `.csproj` even though the local build is fine. The Docker job in CI is what catches that.

The API is the only application image, and the release workflow builds that one: it serves HTTP and
runs the periodic jobs. A separate runner process, if one is ever extracted, would get a Dockerfile of
its own and an entry in the workflow's image matrix.

<!--#if (sonarWorkflow)-->
## Static analysis, and where its verdict lives

SonarQube is wired up, and it is deliberately **not** one of the gates above. Those are the ones you
can run from a clone — no account, no token, no third party. Sonar cannot promise that, so it lives
in its own workflow with its own verdict.

**It can still fail your pull request, and it is meant to.** The job blocks on the quality gate, so
a red gate is a red check. What saves that from being unbearable is that the gate judges **new
code**: its conditions are all `new_*` metrics, so the debt already in the tree blocks nobody, and
nothing dirty gets added on top of it. Fixing an old smell is never the price of merging; adding a
new one is.

`.github/workflows/sonarqube.yml` analyses nothing until the repository is configured for it. The
job is skipped unless the repository variables `SONAR_ORGANIZATION` and `SONAR_PROJECT_KEY` are both
set, and it is skipped for pull requests from forks, which get no secrets. That is the correct state
for a template: a generated project inherits the workflow without inheriting somebody else's
dashboard, and an unconfigured repository is green rather than broken. A hard-coded organisation key
would have been inherited by every generated project, which is why they are variables.

One thing to expect rather than debug: the first analysis of `main` can come back red, because a
project with no previous analysis has no baseline and everything reads as new code. It settles as
soon as that first run becomes the baseline.

The analysis re-builds and re-tests rather than reusing the build job's output: SonarScanner for .NET
learns which file belongs to which project by observing MSBuild between its `begin` and `end`
calls, so a build that already happened is invisible to it. Coverage reaches Sonar through
`sonar.cs.cobertura.reportsPaths`, reading the same Cobertura reports and the same
`coverage.runsettings` the coverage gate uses.

Duplication detection excludes `**/*.html`, which is the mail templates and nothing else. Each is a
standalone document a client receives whole, with its CSS inline because a mail client drops a
linked stylesheet, so the shipped templates share a `<style>` block by construction and the detector
reports each as a near clone of the others. Duplication only: every rule that judges the markup
itself still applies.

### Running it locally

```bash
docker compose --profile sonar up -d --wait sonarqube   # http://localhost:9111, admin/admin
# generate a token under My Account > Security, put it in .env as SONAR_TOKEN
dotnet run Tools/Tasks.cs sonar
```

The profile matters: `compose-up` gives you the application stack, and a 3 GB JVM that only the
analysis needs has no business in it. Port 9111 and not 9000 because the object store already publishes 9000.

**The scanner needs Java, and you do not.** SonarScanner for .NET is a .NET tool that shells out to
a JRE, so `Tools/sonar-scanner.Dockerfile` supplies one. The prerequisites at the top of this page
stay true: the SDK `global.json` pins, and Docker. The scanner's version is pinned in
`.config/dotnet-tools.json` next to `dotnet-ef`, so the scanner CI runs and the scanner your machine
runs are the same one.

Two things about that local run are worth knowing before they surprise you:

- **It reports less coverage than CI does.** There is no Docker daemon inside the scanner container,
  so the Testcontainers suites cannot run there; the set is the one `test --no-integration` uses. CI
  runs the whole suite and is what reports the real figure.
- **It does not touch your `bin/` and `obj/`.** The container builds into `artifacts/sonar`, which is
  git-ignored, so your next build on the machine still works. That path is inside the checkout on
  purpose: the architecture suite finds the repository root by climbing from its own assembly, and
  built anywhere outside the tree every one of its rules fails before it runs.
<!--#endif-->
