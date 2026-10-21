# Using the template

## Install and uninstall

From nuget.org:

```bash
dotnet new install CleanArch.WebApi.Templates
dotnet new update
dotnet new uninstall CleanArch.WebApi.Templates
```

From a package packed locally, as [the package](maintaining-the-template.md#the-package) shows:

```bash
dotnet new install <folder>/CleanArch.WebApi.Templates.<version>.nupkg
```

One install brings three templates: `cleanarch-webapi`, the project, and the item templates
`cleanarch-webapp` and `cleanarch-webpackage`. A checkout installs too, with
`dotnet new install <path-to-this-repository>`, for a maintainer trying a change: the engine then
reads every `.template.config` under the path, a worktree's or a scratch copy's included, which the
package never carries.

## Generate

```bash
dotnet new cleanarch-webapi -n <Company.Product>
cd <Company.Product>
dotnet run Tools/Tasks.cs bootstrap
```

With a checkout installed, generate outside it: the engine reads the template from that path, so
output written inside it is copied into itself and nests a duplicate. Then follow the generated
project's [first run](../getting-started/first-run.md); bootstrap is its first step and is not
optional, since the order of sorted using directives depends on the name chosen.

## What the name substitutes into

`AppTemplate` is the source name. `-n Acme.Shop` turns it into every project, namespace, solution and
file name (the projects become Acme.Shop.Api and the like, the solution Acme.Shop.sln), and its lower-case, hyphenated form `acme-shop` into the
Compose project, the image names and the web workspace's package scope. Every project GUID is
regenerated.

A segment that is a C# or Visual Basic keyword (`Default`, `Shared`) does not build: CA1716 is an
error under warnings as errors. Pick a name with no such segment.

## Options

| Option | Values | Default | Gives |
|---|---|---|---|
| `--auth` | `local`, `external`, `none` | `local` | `local`: the Auth module, its own context and migration, accounts, two-factor, passkeys, roles. `external`: bearer tokens from an OpenID Connect provider, each subject mapped to an owner in the business context. `none`: a public API, no accounts |
| `--examples` | `all`, `notes`, `none` | `all` | TodoLists, Reminders and Notes; Notes alone; no example. Reminders needs TodoLists, so they come together |
| `--files` | `true`, `false` | `true` | the Files feature, its storage module, the object store in Compose and its tables |
| `--ci` | `true`, `false` | `true` | `ci.yml`, `codeql.yml`, `release.yml`, and `web.yml` with a web app |
| `--sonar` | `true`, `false` | `true` | the local SonarQube profile, its scanner image and task, and `sonarqube.yml` when `--ci` is true |
| `--web` | `none`, `placeholder`, `react`, `angular` | `none` | the first web app in the pnpm workspace `Src/Web`; a framework app is created by its official CLI at bootstrap |
| `--web-name` | PascalCase text | `Site` | the first app's folder, Compose service and port variable |
| `--web-audience` | `public`, `admin` | `public` | who the first app is for; `admin` builds a back office |

What an option leaves out is absent, not disabled: no project, file, setting, table, test or page names
it, and the generated solution builds and passes its tests.

## Combinations that are refused

| Combination | Refused by | Fix |
|---|---|---|
| `--auth none` with an example | `--auth-none-needs-examples-none` | add `--examples none` |
| `--auth none` with `--files true` | `--auth-none-needs-files-false` | add `--files false` |
| `--web-audience admin` with `--auth none` | `--admin-web-needs-accounts` | choose `--auth local` or `external` |

Each refusal is a hidden required parameter: `dotnet new` stops with `Missing mandatory option(s)`
naming it, before writing anything. Passing the parameter by hand gets past the engine and generates
`RefusedGenerationOptions.cs`, whose `#error` fails the build with the same explanation.
[Template decision 0001](decisions/0001-options-remove-parts-and-refuse-combinations.md) gives the
reasons.

## What a project receives and what stays here

A project receives the code, the tests, the tools it judges itself with (`Tasks.cs`, the generator and
its undo, the coverage gate, the test summary), its workflows, and the documentation under `docs/`,
conditioned on its options. Its `README.md` is the project's own, from
`.template.config/variants/project/README.md`.

What maintains the template stays here: `docs/template/`, this repository's `README.md`, the
changelog, the plans, the package project `.template.config/TemplatePackage.csproj`,
`template-acceptance.yml`, `template-release.yml`, the issue forms under `.github/ISSUE_TEMPLATE`,
and the three tree-auditing gates `Tools/CheckDocPaths.cs`, `Tools/CheckWorkflows.cs` and
`Tools/CheckComments.cs`.
[Template decision 0002](decisions/0002-nothing-about-the-template-reaches-a-project.md) records why.

## Item templates

Run both at the root of a generated project, with its workspace scope:

```bash
dotnet new cleanarch-webapp -n BackOffice --framework react --scope <scope> --audience admin
dotnet new cleanarch-webpackage -n tokens --scope <scope>
```

| Template | Options |
|---|---|
| `cleanarch-webapp` | `--framework placeholder\|react\|angular` (required), `--audience public\|admin`, `--scope`, `--port`, and the project's `--auth` and `--ci` when they are not the defaults. `--workspace` also creates `Src/Web` and `web.yml`, for the first app of a project generated with `--web none`; it is refused where `Src/Web` exists |
| `cleanarch-webpackage` | `--scope`: a framework-agnostic TypeScript package, exporting its sources, with one function and its test |

An item template can add files but not edit them, so each prints the lines to add to
`docker-compose.yml`, `.env.example` and, with `--workspace`, `.dockerignore` and
`.github/dependabot.yml`; the generated `Src/Web/README.md` lists them.
