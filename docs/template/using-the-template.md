# Using the template

## With the launcher

With a .NET 10 SDK (10.0.100 or later), one command asks for the name and every option, then
generates the project:

```bash
dnx CleanArch.Create
```

`CleanArch.Create` is a .NET tool, run without being installed. Its questions are the options this
page lists, read from the templates' own manifests with their choices, defaults and descriptions. It
offers only the choices the template generates with the answers given so far, answers a question
left with one choice and says which refusal leaves it, explains a refused combination of flags
before writing anything and asks its options again, installs
`CleanArch.WebApi.Templates` at its own version when another one or none is installed, runs
`dotnet new cleanarch-webapi`, and prints the equivalent commands and the first steps. In a
terminal the questions follow one another on a thread under a banner, each with a line naming its
keys; `esc` goes back to the previous question, whose answer it keeps selected, and the questions
after it are asked again. A summary of every option comes before anything is written, and answering
No to it goes back to the last question; with `--yes`, without a terminal, or with `NO_COLOR` set,
the output stays plain text.

Its last question offers to set the project up as [the first run](../getting-started/first-run.md)
asks: a first commit, after `git init` unless the folder is in a work tree already, then
`dotnet run Tools/Tasks.cs bootstrap`, a commit of what it wrote, and `.env` copied from
`.env.example`. The commits carry your own git identity, a step that fails says why without stopping
the independent ones, and the commands printed at the end are only those left to run. Without
questions, `--setup` runs every step.

At the root of a generated project, or in any folder under it, it adds a web app or a package to the
workspace: the project's authentication, workflows and scope are read from its files, and the first
app also creates the workspace. It then prints the lines its `README.md` asks to add by hand.

It adds a presentation project to a project generated without one the same way: the project's name,
authentication, example features, Files module, database and agent files are read from its files, so
it asks only the presentation name, and the framework once there is more than one. After
`dotnet new cleanarch-presentation` it runs the project's `presentation-join` task, which makes the
lines the new `README.md` lists outside the project's folders, then `presentation-scaffold`, which
needs the network; a step that fails says why and what to run again.

```bash
dnx CleanArch.Create web-app
dnx CleanArch.Create web-package
dnx CleanArch.Create presentation
```

Each question is also a flag named like the option, and `--yes` takes the default of each one not
given. Without a terminal, every answer is a flag or `--yes` is passed:

```bash
dnx CleanArch.Create new -n Acme.Shop --auth external --examples notes --yes
dnx CleanArch.Create web-app -n BackOffice --framework vite-react --audience admin --yes
dnx CleanArch.Create presentation -n Portal --yes
```

`--template-source <path>` installs the templates from a checkout or a `.nupkg` instead of the
published package, and `--hive <folder>` keeps the template engine's settings apart, as
`--debug:custom-hive` does. Everything below is `dotnet new` itself, which the launcher runs.
[Template decision 0004](decisions/0004-the-launcher-reads-the-templates-manifests.md) records why
the launcher reads the manifests rather than copying them.

## Install and uninstall

From nuget.org, where each release is published:

```bash
dotnet new install CleanArch.WebApi.Templates
dotnet new update
dotnet new uninstall CleanArch.WebApi.Templates
```

`dotnet new update` moves an installed package to its latest version.

From a GitHub Release: download the asset `CleanArch.WebApi.Templates.<version>.nupkg` from the
release `v<version>` on the repository's Releases page, then, in the folder holding it:

```bash
dotnet new install ./CleanArch.WebApi.Templates.<version>.nupkg
dotnet new uninstall CleanArch.WebApi.Templates
```

From nuget.org, once a version is published there, which also lets `dotnet new update` find the
next one:

```bash
dotnet new install CleanArch.WebApi.Templates
dotnet new update
```

A package packed locally, as [the package](maintaining-the-template.md#the-package) shows, installs
like a release asset, from its path.

One install brings four templates: `cleanarch-webapi`, the project, and the item templates
`cleanarch-webapp`, `cleanarch-webpackage` and `cleanarch-presentation`. A checkout installs too, with
`dotnet new install <path-to-this-repository>`, for a maintainer trying a change: the engine then
reads every `.template.config` under the path, a worktree's or a scratch copy's included, which the
package never carries. Both leave out the editor and assistant folders of the checkout, which
are `.claude`, `.idea`, `.vs` and `.vscode`.

## In an IDE

Once installed, the four templates are listed by Visual Studio's new-project dialog, by Rider's, and
by the C# Dev Kit's "New Project" command in VS Code, with the options under their labels and the
refusals hidden. None of them needs a separate extension: each reads the templates `dotnet new`
installed. Visual Studio takes its labels from `.template.config/ide.host.json`; the others from the
`displayName` of each option in `template.json`.

## Generate

```bash
dotnet new cleanarch-webapi -n <Company.Product>
cd <Company.Product>
dotnet run Tools/Tasks.cs bootstrap
```

With a checkout installed, generate outside it: the engine reads the template from that path, so
output written inside it is copied into itself and nests a duplicate. Then follow the generated
project's [first run](../getting-started/first-run.md); bootstrap is its first step and is not
optional, since the order of sorted using directives depends on the name chosen and the project ships
no migration until bootstrap creates its first one.

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
| `--database` | `postgresql`, `sqlserver`, `mysql`, `sqlite` | `postgresql` | `postgresql`: a schema per feature, the row's `xmin` as its version, advisory locks for the leases. `sqlserver`: schemas, a version counter the save stamps, application locks (`sp_getapplock`). `sqlite`, for development only: one file, a table prefix per feature (`<schema>_<Table>`), in-process leases, and an API that refuses to start outside Development. A project carries its provider only, and `mysql` is refused: this version does not generate it |
| `--files` | `true`, `false` | `true` | the Files feature, its storage module, the object store in Compose and its tables |
| `--ci` | `true`, `false` | `true` | `ci.yml`, `codeql.yml`, `release.yml` (not with `--database sqlite`), and `web.yml` with a web app |
| `--sonar` | `true`, `false` | `true` | the local SonarQube profile, its scanner image and task, and `sonarqube.yml` when `--ci` is true |
| `--agent-files` | `true`, `false` | `true` | `AGENTS.md`, and `Src/Web/AGENTS.md` with a web app; it is the only agent file the template writes, and the pages that point to it point to the page that owns the rule when it is absent |
| `--web` | `none`, `placeholder`, `vue`, `vite-react`, `vite-vue`, `vite-svelte`, `react-router`, `angular`, `react`, `blazor-wasm` | `none` | the first web app in `Src/Web`; a framework app is created by its official CLI or template at bootstrap, as the table below says; `blazor-wasm` calls the API through the .NET client and is refused with `--auth none` |
| `--web-name` | PascalCase text | `Site` | the first app's folder, Compose service and port variable |
| `--web-audience` | `public`, `admin` | `public` | who the first app is for; `admin` builds a back office |
| `--web-rendering` | `spa`, `ssr` | `spa` | how the first app renders: `ssr` renders each page on its framework's own Node server, with a nonce per response and, with local accounts, the session, for `angular`, not yet with `--auth external` |
| `--presentation` | `none`, `blazor-server`, `avalonia` | `none` | a presentation project in `Src/Presentation`; `blazor-server` is a Blazor Web App with server interactivity that calls the use cases in process, its pages written by `dotnet new blazor` at bootstrap; `avalonia` is a desktop app that calls the API through the .NET client, its views written by `dotnet new avalonia.mvvm`, refused with `--auth none` |
| `--presentation-name` | PascalCase text | `Portal` | the presentation project's name after the solution's, its Compose service and port variable |

Each framework value of `--web`, and of `cleanarch-webapp --framework`, names the generator that
writes the app and never changes meaning:

| Value | Generator | Template or flags | Dev port |
|---|---|---|---|
| `vue` | `create-vue`, Vue's official CLI | TypeScript, Vue Router, Vitest, ESLint | 5173 |
| `vite-react` | `create-vite`, Vite's official CLI | `react-ts` | 5173 |
| `vite-vue` | `create-vite` | `vue-ts` | 5173 |
| `vite-svelte` | `create-vite` | `svelte-ts` | 5173 |
| `react-router` | `create-react-router`, React Router's official CLI | its default template at a fixed commit, single-page mode (`ssr: false`) | 5173 |
| `angular` | `ng new`, the Angular CLI | standalone, routing, CSS, no server-side rendering, Vitest; with `ssr`, `--ssr` and every route rendered per request | 4200 |
| `react` | the same app as `vite-react`, under the name it had first; kept until the next major version | | 5173 |
| `blazor-wasm` | `dotnet new blazorwasm`, Microsoft's template | `--auth None --no-https`, or `--auth Individual --no-https` under `--auth external`, signing the user in at the provider; the project's own `Program.cs` composing the .NET client, which `cleanarch-webapp` brings with the app | 8081, through the stack |

What an option leaves out is absent, not disabled: no project, file, setting, table, test or page names
it, and the generated solution builds and passes its tests.

## Combinations that are refused

| Combination | Refused by | Fix |
|---|---|---|
| `--auth none` with an example | `--auth-none-needs-examples-none` | add `--examples none` |
| `--auth none` with `--files true` | `--auth-none-needs-files-false` | add `--files false` |
| `--web-audience admin` with `--auth none` | `--admin-web-needs-accounts` | choose `--auth local` or `external` |
| `--web-rendering ssr` with a `--web` other than `angular` | `--server-rendering-needs-a-server-starter` | choose `--web angular`, or `--web-rendering spa` |
| `--web-rendering ssr` with `--auth external` | `--server-rendering-with-external-auth-not-yet-generated` | choose `--auth local` or `none`, or `--web-rendering spa` |
| `--database mysql` | `--my-sql-not-yet-generated` | choose `--database postgresql` |
| a `--presentation-name` that names one of the template's projects, such as `Api` | `--presentation-name-is-taken` | choose another name, such as `Portal` |

Each refusal is a hidden required parameter: `dotnet new` stops with `Missing mandatory option(s)`
naming it, before writing anything. Passing the parameter by hand gets past the engine and generates
`RefusedGenerationOptions.cs`, whose `#error` fails the build with the same explanation.
[Template decision 0001](decisions/0001-options-remove-parts-and-refuse-combinations.md) gives the
reasons.

`cleanarch-presentation` refuses the same combinations of the project's options it repeats, and as
`-n` each name `--presentation-name` refuses.

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

Run each at the root of a generated project, the web ones with its workspace scope:

```bash
dotnet new cleanarch-webapp -n BackOffice --framework vite-react --scope <scope> --audience admin
dotnet new cleanarch-webpackage -n tokens --scope <scope>
dotnet new cleanarch-presentation -n Portal --framework blazor-server --project-name <Project>
```

| Template | Options |
|---|---|
| `cleanarch-webapp` | `--framework placeholder\|vue\|vite-react\|vite-vue\|vite-svelte\|react-router\|angular\|react\|blazor-wasm` (required), `--audience public\|admin`, `--rendering spa\|ssr` (`ssr` for `angular`, not yet in a project with `--auth external`), `--scope`, `--port`, and the project's `--auth` and `--ci` when they are not the defaults. `blazor-wasm` also takes `--project-name` (required: the project's `-n`), the project's `--examples`, `--files` and `--agent-files`, and `--has-client true` where the project holds the .NET client already, which it then uses instead of writing; it is refused with `--auth none`. `--workspace` also creates `Src/Web` and `web.yml`, for the first app of a project generated with `--web none`; it is refused where `Src/Web` exists |
| `cleanarch-webpackage` | `--scope`: a framework-agnostic TypeScript package, exporting its sources, with one function and its test |
| `cleanarch-presentation` | `--framework blazor-server\|avalonia` (required), `--project-name` (required: the project's `-n`, which names `<Project>.<name>`), and the project's `--auth`, `--examples`, `--files`, `--database` and `--agent-files` when they are not the defaults, with the same values and refusals; `avalonia` takes `--has-client true` where the project holds the .NET client already. `-n` is the presentation project's name, `Portal` by default, refused as `--presentation-name` refuses it: the presentation project of a project generated with `--presentation none` |

An item template can add files but not edit them, so each prints the lines to add to
`docker-compose.yml`, `.env.example` and, with `--workspace`, `.dockerignore` and
`.github/dependabot.yml`; the generated `Src/Web/README.md` lists them. `cleanarch-presentation`
writes the project, its tests and its page, and the `README.md` in the project's folder lists every
line `--presentation` writes elsewhere, each placed by the lines around it: `.dockerignore`,
`.env.example`, `Directory.Packages.props` under external authentication, `docker-compose.yml`,
`AGENTS.md`, two architecture tests and the pages under `docs/`.
`dotnet run Tools/Tasks.cs presentation-join <Project>.<name>` makes them all, or none when a line
to place one by is not found exactly once, and leaves them alone once they are in place. Then
`dotnet run Tools/Tasks.cs presentation-scaffold <Project>.<name>` creates it, as bootstrap does.
`cleanarch-webapp --framework blazor-wasm` writes the app, the .NET client and its tests, and the
app's `README.md` lists the lines `--web blazor-wasm` writes elsewhere the same way;
`dotnet run Tools/Tasks.cs web-join <name>` makes them, before `web-scaffold` creates the app.
