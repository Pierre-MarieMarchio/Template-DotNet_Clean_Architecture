# Contributing

What has to be true before a change is done, and where each rule is written.

## The gate

A change is done when `dotnet run Tools/Tasks.cs verify`, then `dotnet run Tools/Tasks.cs coverage`,
pass without overrides; [build and CI](docs/guides/build-and-ci.md#the-gate) says what each step
checks, and [first run](docs/getting-started/first-run.md) what the machine needs.

## Rules and where they live

Each rule has one home, and a test holds it wherever a test can.

| Rule | Home |
|---|---|
| How to work here: the commands, where code goes, what done means | [AGENTS.md](AGENTS.md) |
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
| How a page is written, and which pages move with the code | [AGENTS.md › Writing docs](AGENTS.md#writing-docs), [AGENTS.md › Done means](AGENTS.md#done-means) |
| Why a choice was made | [Decisions](docs/decisions/README.md) |
| What a deployment still owes | [SECURITY.md](SECURITY.md) |

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
