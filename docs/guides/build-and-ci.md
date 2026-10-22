# Build and CI

## The task launcher

`Tools/Tasks.cs` wraps the `dotnet` and `docker` commands of this project and prints each one before
running it, so the printed line gives the same result without the launcher. No task hides a flag that
changes the meaning of a build. `dotnet run Tools/Tasks.cs help` lists them with their options.

| Task | Runs |
|---|---|
<!--#if (anyWeb)-->
| `bootstrap` | format, write the OpenAPI reference, restore the local tools, `migration-initial`, then `web-scaffold`; once, after generation |
<!--#endif-->
<!--#if (!anyWeb)-->
| `bootstrap` | format, write the OpenAPI reference, restore the local tools, then `migration-initial`; once, after generation |
<!--#endif-->
| `restore`, `build`, `test`, `format`, `format-fix` | the `dotnet` command of the same name over the solution; `test --no-integration` skips the projects that need Docker |
| `coverage` | the tests with coverage, against `coverage.minimum` |
| `run` | the API on the host |
| `compose-up`, `compose-down` | the local stack, waiting until every service is healthy |
| `end-to-end` | the end-to-end suite against a Compose stack of its own, removed afterwards unless `--keep-stack` |
| `new-feature`, `remove-feature` | the feature generator and its undo |
| `new-use-case` | one more use case in a feature of the rich shape |
| `migration-initial` | each context's initial migration, where its `Migrations` folder holds none |
| `migration-add`, `database-update` | `dotnet ef` over the business context, once it has its initial migration |
<!--#if (serverDatabase)-->
| `migration-bundle` | one self-contained migration bundle per context, under `artifacts/migrate` |
<!--#endif-->
| `openapi` | rewrite the reviewed OpenAPI reference the contract test compares the served document with |
<!--#if (sonar)-->
| `sonar` | the local Sonar analysis |
<!--#endif-->
<!--#if (anyWeb)-->
| `web-scaffold` | create each web app still holding its scaffold.json |
<!--#endif-->
<!--#if (!anyWeb)-->
| `web-scaffold` | create the framework app a first `cleanarch-webapp --workspace` adds |
<!--#endif-->
| `hygiene` | the self-tests of the gates this tree carries |
| `verify` | the whole gate, in CI's order |

Everything under `Tools/` is a file-based C# app: the SDK `global.json` pins is all it needs. The
first run of a file compiles it, which takes a moment.

## The gate

A change is done when this passes, without overrides:

```bash
dotnet run Tools/Tasks.cs verify
```

It refuses to start while a context has no migration, then runs `hygiene`, restore,
`dotnet format --verify-no-changes`, the build, the tests, the migration drift check for every
context, and the vulnerability listing. The coverage floor is a
separate task, `coverage`.

On a machine short of memory, `-m:<N>` caps `restore`, `build`, `test`, `coverage` and `verify` at
N MSBuild nodes and N test projects running at once, as in `dotnet run Tools/Tasks.cs verify -m:4`.

Each gate proves it can fail before it judges the tree: `hygiene` runs the `--self-test` of the
generator, the removal command and the coverage gate, each against faulted and sound fixtures. A gate
also refuses to pass on nothing: a test run that executed no test and a coverage total over no
assembly both fail. A rule added to a gate comes with the fixture that fails without it.

`TreatWarningsAsErrors` is set in `Directory.Build.props` for every project. Do not silence a
diagnostic with `#pragma`, `<NoWarn>` or a lowered severity; a diagnostic wrong for one file gets a
path-scoped `.editorconfig` section with its reason. Every project generates its documentation file,
which is what makes an unused `using` (IDE0005) fail the build.

`.cs` and `.md` files are UTF-8 with BOM. A file created without one fails the formatting gate;
`format-fix` adds it.

<!--#if (ci)-->
## Workflows

| Workflow | When | Does |
|---|---|---|
| `.github/workflows/ci.yml` | push and pull request to `main` | restore, formatting, build, every test project in the solution, tests with coverage and the floor; the `hygiene` gates; the vulnerability listing; the API image, unprivileged and exposing 8080 only; the Compose manifest; the end-to-end suite against the stack; the load smoke |
<!--#endif-->
<!--#if (ci && serverDatabase)-->
| `.github/workflows/release.yml` | a `v*.*.*` tag | the gate again, the migration bundles, and the image pushed to GHCR |
<!--#endif-->
<!--#if (ci && !anyWeb)-->
| `.github/workflows/codeql.yml` | push and pull request to `main`, and weekly | CodeQL code scanning of the C# the solution and `Tools/` compile; alerts land in the repository's Security tab |
<!--#endif-->
<!--#if (ci && anyWeb)-->
| `.github/workflows/codeql.yml` | push and pull request to `main`, and weekly | CodeQL code scanning of the C# the solution and `Tools/` compile, and of the TypeScript in `Src/Web`; alerts land in the repository's Security tab |
<!--#endif-->
<!--#if (ci && anyWeb)-->
| `.github/workflows/web.yml` | push and pull request to `main` | in `Src/Web`: frozen install, lint, test, build, and a failure when the API client differs from the API's document; one image per app |
<!--#endif-->
<!--#if (sonarWorkflow)-->
| `.github/workflows/sonarqube.yml` | push and pull request to `main` | the SonarQube Cloud analysis, once its variables are set |
<!--#endif-->
<!--#if (ci)-->

The load smoke never gates: its thresholds describe a broken system, and a `429` counts as a pass,
because a hosted runner's timings say nothing about this code. Run it against your own stack with
`k6 run -e BASE_URL=http://localhost:8080 Tests/Load/smoke.js`.

Every action is pinned to a commit SHA and every image to a digest, `permissions` is
`contents: read` unless a job needs more, and a newer run on a branch other than `main` cancels the older one.
<!--#endif-->

<!--#if (sonar)-->
## Sonar

Sonar is not one of the gates: it needs a server and a token, which a clone does not have.
<!--#endif-->
<!--#if (sonarWorkflow)-->
In CI it can still fail a pull request: the quality gate judges new code only, so the code already
there blocks nobody. `.github/workflows/sonarqube.yml` is skipped until the repository variables
`SONAR_ORGANIZATION` and `SONAR_PROJECT_KEY` are set, and for a fork's pull request; it reads the
secret `SONAR_TOKEN`. The first analysis of `main` can come back red, since everything is new code
until a baseline exists.
<!--#endif-->
<!--#if (sonar)-->

Locally:

```bash
docker compose --profile sonar up -d --wait sonarqube   # http://localhost:9111, admin/admin
dotnet run Tools/Tasks.cs sonar                         # after setting SONAR_TOKEN in .env
```

The scanner needs a JRE, which `Tools/sonar-scanner.Dockerfile` supplies, so your machine needs none.
It builds into `artifacts/sonar`, leaving your `bin/` and `obj/` alone. It runs the Docker-free tests
only, so it reports less coverage than CI.

<!--#endif-->
<!--#if (ci && serverDatabase)-->
## Release

Push a tag such as `v1.2.3`. The release re-runs the gate, then publishes the API image to GHCR as
`1.2.3`, `1.2`, `1` and `latest` (a prerelease tag never moves `latest`), multi-arch, with an SBOM and
a signed provenance attestation, and uploads the migration bundles built from the same commit. It
needs no secret beyond `GITHUB_TOKEN`. Applying the bundles is yours:
[deployment](deployment.md#migrations-at-release).

<!--#endif-->
## Supply chain

Every dependency is at an exact version, and `.github/dependabot.yml` proposes updates weekly, in
bands; nothing is ignored.

| Dependency | Pinned in |
|---|---|
| NuGet packages | `Directory.Packages.props` |
| The SDK | `global.json` |
| The .NET tools | `.config/dotnet-tools.json` |
| Container images and actions | their digest or commit SHA, beside the tag |
<!--#if (anyWeb)-->
| Node, pnpm and the web packages | `Src/Web/.node-version`, `Src/Web/package.json`, `Src/Web/pnpm-lock.yaml` |
<!--#endif-->

`NuGetAudit` fails the build on any known advisory, `low` included. Most arrive through a package
nobody references directly; the answer is a transitive pin in `Directory.Packages.props` with a
guidance line saying when it can go, never a relaxed audit level. Stay on the major the referencing
package was built against. `dotnet list package --vulnerable --include-transitive` exits zero
whatever it finds, so `verify` and CI read what it prints.

## Decisions

The toolchain, the pinning and Dependabot are rows of
[ADR 0001](../decisions/0001-adopt-the-generated-baseline.md).
