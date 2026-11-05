# CleanArch.WebApi.Templates

[![NuGet](https://img.shields.io/nuget/v/CleanArch.WebApi.Templates?logo=nuget&label=NuGet)](https://www.nuget.org/packages/CleanArch.WebApi.Templates)
[![CI](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/actions/workflows/ci.yml?query=branch%3Adevelop)
[![Template acceptance](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/actions/workflows/template-acceptance.yml/badge.svg?branch=develop)](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/actions/workflows/template-acceptance.yml?query=branch%3Adevelop)
[![MIT licence](https://img.shields.io/badge/licence-MIT-blue)](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/blob/main/LICENSE)
[![Support on Ko-fi](https://img.shields.io/badge/Ko--fi-support-FF5E5B?logo=kofi&logoColor=white)](https://ko-fi.com/pmmarchio)

The `dotnet new` templates for a .NET 10 project on Clean Architecture whose project is meant to be
kept: an HTTP API, or hosts with no API, such as a Blazor portal, an MCP server, a desktop app or a
worker. Its modules, mechanisms, tests, tools and documentation are the ground a product grows on,
and the options remove what a product does not need.

The package holds four templates:

| Template | Generates |
|---|---|
| `cleanarch-webapi` | the solution: four layers held by architecture tests, EF Core on PostgreSQL, SQL Server or SQLite, local, external or no authentication, Files, jobs, mail, CI and documentation |
| `cleanarch-webapp` | a web app in the project's pnpm workspace |
| `cleanarch-webpackage` | a package in that workspace |
| `cleanarch-presentation` | a presentation project in a solution generated without one |

## Getting started

You need a .NET 10 SDK (10.0.100 or later), and Docker with Compose v2 to run the stack. The
`CleanArch.Create` launcher asks for each option, installs this package and generates the project:

```bash
dnx CleanArch.Create
```

Or with `dotnet new` alone:

```bash
dotnet new install CleanArch.WebApi.Templates
dotnet new cleanarch-webapi -n Company.Product
cd Company.Product
dotnet run Tools/Tasks.cs bootstrap
cp .env.example .env
docker compose up -d --build --wait
```

## Options

Each part is an option: `--auth`, `--database`, `--examples`, `--files`, `--web`, `--presentation`
and more, with the combinations the template refuses explained before anything is written.
[Using the template](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/blob/main/docs/template/using-the-template.md)
lists every value and what it generates.

## Documentation

- [The repository](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet): the overview, the
  options and the roadmap.
- [The documentation index](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/blob/main/docs/README.md):
  the pages every generated project receives, its architecture and its decisions.
- [The changelog](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/blob/main/CHANGELOG.md).

## Support

Report a bug or ask for a feature through
[the issue forms](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/issues/new/choose);
report a vulnerability privately, in a
[security advisory](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/security/advisories/new).
If the template saves your team time, you can support its upkeep on
[Ko-fi](https://ko-fi.com/pmmarchio).

MIT licence.
