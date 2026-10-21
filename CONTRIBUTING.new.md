# Contributing

What has to be true before a change is done, and where each rule is written.

## A green build

```bash
dotnet run Tools/Tasks.cs bootstrap   # once, after a fresh clone of a new project
dotnet run Tools/Tasks.cs build
dotnet run Tools/Tasks.cs test        # Docker for the integration suites
```

The SDK `global.json` pins and Docker are all it needs: everything under `Tools/` is a file-based C#
app. [First run](docs/getting-started/first-run.md) starts the stack.

## The gate

```bash
dotnet run Tools/Tasks.cs verify
dotnet run Tools/Tasks.cs coverage
```

`verify` runs, in CI's order, the self-tests of the gates, restore, formatting, the build with warnings
as errors, the tests, the migration drift check for every context and the vulnerability listing.
`coverage` holds the floor in `coverage.minimum`. Both pass without overrides: no `#pragma`, no
`<NoWarn>`, no lowered severity, no skipped test. [Build and CI](docs/guides/build-and-ci.md) says what
each step checks.

`.cs` and `.md` files are UTF-8 with BOM; `dotnet run Tools/Tasks.cs format-fix` after creating or
moving a file.

## Rules and where they live

Each rule has one home, and a test holds it wherever a test can.

| Rule | Home |
|---|---|
| What may depend on what | [Architecture](docs/architecture/README.md), held by `Tests/Architecture` |
| Which project and folder a file goes in, and its name | [Project layout](docs/reference/project-layout.md) |
| A feature's shape: an aggregate, or a service over a store | [Feature shapes](docs/architecture/feature-shapes.md) |
| The contract every endpoint offers | [HTTP API](docs/architecture/http-api.md) |
| Code carries no comment; reasons live in ADRs and pages | [AGENTS.md › Changing code](AGENTS.md#changing-code), [ADR 0001](docs/decisions/0001-adopt-the-generated-baseline.md) |
| Extract an abstraction only once two real cases prove it | [AGENTS.md › Changing code](AGENTS.md#changing-code) |
| How tests are written | [Testing › House rules](docs/guides/testing.md#house-rules) |
| Adding and removing a feature | [Adding a feature](docs/guides/adding-a-feature.md), [Removing features](docs/guides/removing-features.md) |
| Migrations | [Migrations](docs/guides/migrations.md) |
| Every setting | [Configuration](docs/reference/configuration.md), held by `ConfigurationSurfaceTests` |
| Why a choice was made | [Decisions](docs/decisions/README.md) |
| What a deployment still owes | [SECURITY.md](SECURITY.md) |

## Documentation is part of the change

A change that moves a file updates every page that cites it. A change that adds a capability adds its
page. A choice another team could have made differently adds an ADR, with the test that holds it.
[AGENTS.md › Writing docs](AGENTS.md#writing-docs) holds the writing rules, and `docs/_templates/`
the skeleton of each kind of page.

## Pull requests

- One concern per pull request: a refactor and a behaviour change in one diff cannot be reviewed.
- The gate passes, without overrides.
- New behaviour comes with a test you have seen fail: break the code, watch the test go red, restore
  it.
- A constraint you checked by hand becomes a rule in `Tests/Architecture`.
- A decision that could reasonably have gone the other way is an ADR.

<!--#if (templateMaintenance)-->
## Working on the template

This repository is also the template. Its own gates, the acceptance matrix and the rules for writing
pages that condition on the generation options are in
[maintaining the template](docs/template/maintaining-the-template.md).
<!--#endif-->
