# Agents

<!--#if (api && localAuth)-->
This project is a .NET 10 HTTP API on Clean Architecture whose callers sign in with local accounts.
<!--#endif-->
<!--#if (api && externalAuth)-->
This project is a .NET 10 HTTP API on Clean Architecture whose callers bring an OpenID Connect token.
<!--#endif-->
<!--#if (api && publicApi)-->
This project is a .NET 10 HTTP API on Clean Architecture that authenticates nobody.
<!--#endif-->
<!--#if (!api && localAuth)-->
This project is a set of .NET 10 hosts on Clean Architecture, with no HTTP API, whose users sign in with local accounts.
<!--#endif-->
<!--#if (!api && externalAuth)-->
This project is a set of .NET 10 hosts on Clean Architecture, with no HTTP API, whose users sign in with an OpenID Connect provider.
<!--#endif-->
<!--#if (!api && publicApi)-->
This project is a set of .NET 10 hosts on Clean Architecture, with no HTTP API, that authenticates nobody.
<!--#endif-->
Your request comes first, then the `AGENTS.md` nearest the file you change, then this one; a folder
holding its own, such as the web workspace when there is one, adds what that folder needs.

## Start here

[The documentation index](docs/README.md) says which page answers which question: open the page
before reading code to find the answer. Before you:

| Do this | Read |
|---|---|
| Add your first type | [the overview](docs/getting-started/overview.md), then [the architecture](docs/architecture/README.md) |
| Add or change a feature | [adding a feature](docs/guides/adding-a-feature.md), [feature shapes](docs/architecture/feature-shapes.md) |
| Call another feature | [reaching another feature](docs/guides/reaching-another-feature.md) |
| Change the model | [migrations](docs/guides/migrations.md) |
| Add a setting | [adding a setting](docs/guides/adding-a-setting.md) |
| Write a test | [the house rules](docs/guides/testing.md#house-rules) |
| Ask why a choice was made | [the decisions](docs/decisions/README.md) |

When two sources disagree, the first wins and the other is a defect to fix in the same change: tests
and code, then the ADRs in `docs/decisions/`, then the pages, then the tables in `docs/reference/`.

## Commands

| When | Run |
|---|---|
| Once, right after generating | `dotnet tool restore`, then `dotnet cleanarch bootstrap`; commit what it writes |
| While you work | `dotnet cleanarch check`: format, build and tests of what you touched |
| Over the whole tree, without Docker | `dotnet cleanarch test --no-integration` |
| After creating or moving a file | `dotnet cleanarch format` |
| After adding a page under `docs/` or changing its frontmatter | `dotnet cleanarch docs-index` |
| After changing the model | `dotnet cleanarch migrations add <Name>` |
| <!--#if (api)--> |
| After changing a route or a contract | `dotnet cleanarch openapi`: the reference and all it generates |
| After changing the stack, a mail or a relay | `dotnet cleanarch end-to-end`, with Docker |
| <!--#endif--> |
| <!--#if (aspire)--> |
| The stack with its dashboard; a Compose service goes there too | `dotnet run --project src/AppHost/AT.AppHost` |
| <!--#endif--> |
| Before calling a change done | `dotnet cleanarch verify`, then `dotnet cleanarch coverage`, with Docker |
| Before taking a newer template version | `dotnet cleanarch upgrade-report --keep <folder>`, which writes nothing |
| Any other command | `dotnet cleanarch help --json`, or [the project tool](docs/reference/cli.md) |

## Generators first

<!--#if (canAddFeature)-->
A feature, a field, a use case, a setting or an event comes from its generator, which writes its
tests, its page and every registration; write by hand only what it leaves to you.

```bash
dotnet cleanarch add feature <Features> <Feature> --access owner --deletion delete|anonymise
dotnet cleanarch add feature <Features> <Feature> --access shared --fields "Title:string(120), Budget:decimal>=0"
dotnet cleanarch add field <Features> "Code:string(20)='none', Notes:text?"
dotnet cleanarch add use-case <Features> <UseCase> --kind command|query
dotnet cleanarch add setting <Features> <Section> --keys "MaxItems:int(1..100)=10"
dotnet cleanarch add event <Features> <Event> --consumer <Features>:<Reaction>
dotnet cleanarch remove feature <Features>   # reports; --apply removes it, once committed
```

- Run it with `--dry-run` first; it writes everything or nothing, and refuses while a model change
  waits for a migration.
- Without `--fields`, the fields are `Name` and `Description`: placeholders to rewrite.
- `add use-case` and `add event` need the rich shape; promote a simple feature first.
<!--#endif-->
<!--#if (!canAddFeature)-->
This project authenticates nobody, so `add feature` refuses it: write a feature by hand from
[adding a feature](docs/guides/adding-a-feature.md), its page from `docs/_page-templates/feature.md`.
`dotnet cleanarch add use-case <Features> <UseCase> --kind command|query` then adds an operation to
it once it has the rich shape, and `--dry-run` shows what it would write.
<!--#endif-->

## Where code goes

- Domain references nothing; Application, the domain; Infrastructure, the application; Presentation,
  the application, and only a host references the infrastructure modules.
- [Project layout](docs/reference/project-layout.md): the project, folder and name of each file.
- [Feature shapes](docs/architecture/feature-shapes.md): an aggregate when the feature has rules that
  protect a state, a service over a store otherwise; size does not enter into it.
- [Modules and boundaries](docs/architecture/modules-and-boundaries.md): how features meet.
<!--#if (api)-->
- [HTTP API](docs/architecture/http-api.md): the contract every endpoint offers.
<!--#endif-->
- A registration, an anonymous endpoint, a folder word, a domain event or a project joins a list a
  test reads: [the closed lists](docs/guides/adding-a-feature.md#the-closed-lists-a-feature-joins).

## Changing code

- The code reads by itself. A comment says only what the code cannot: a reason, an outside
  constraint, a reference. One that explains what the code does asks for a rename or an extraction.
- Extract an abstraction only once two real cases prove its shape.
- A test that buys a guarantee is seen failing once: break the code, watch it go red, restore it.
- A rule checked by hand becomes a test in `tests/Architecture`; a decision is an ADR and the test
  that holds it, changed together.

## Never

- Silence a diagnostic with `#pragma`, `<NoWarn>`, a lowered severity or a skipped test: fix the
  code, or ask. [The gate](docs/guides/build-and-ci.md#the-gate) lists the silences kept.
<!--#if (canAddFeature)-->
- Edit by hand what a command writes: `.feature-manifest/`, `cleanarch.json`, a folder's
  `docs/<folder>/index.md`; nor rewrite a migration a database has applied.
<!--#endif-->
<!--#if (!canAddFeature)-->
- Edit by hand what a command writes: `cleanarch.json`, a folder's `docs/<folder>/index.md`; nor
  rewrite a migration a database has applied.
<!--#endif-->
- Lower `coverage.minimum` to pass: [re-measure it](docs/guides/testing.md#coverage).
<!--#if (accounts)-->
- Answer `403` for another caller's record: it answers `404`, the owner being in the query
  ([ADR 0004](docs/decisions/0004-ownership-is-in-the-query.md)).
<!--#endif-->

## Writing docs

- One question per page, in the voice of "this project": no history, no plan vocabulary.
- A page starts from `docs/_page-templates/` with [its frontmatter](docs/README.md#adding-a-page),
  then `dotnet cleanarch docs-index` lists it.
- A choice another team could have made otherwise is an ADR: [the criteria](docs/decisions/README.md).
- Every path in backticks exists; write an illustration as a placeholder (`<Feature>`) or as prose.

## Done means

- `verify`, then `coverage`, pass without overrides.
<!--#if (ci)-->
  `.github/workflows/ci.yml` runs the same gate on every pull request.
<!--#endif-->
- Without Docker, or when a gate stops on the machine rather than on a test, run what can run
  (`test --no-integration`, the failing step by hand) and say what did not run and why.
- The report names what ran, with its result, and the test that holds each guarantee: a change that
  compiles proves nothing.
- The docs move with the code: a moved file's citations, a new capability's page, a new setting's
  row, a new choice's ADR.
- One concern per change, as [pull requests](CONTRIBUTING.md#pull-requests) asks.
- No `Co-Authored-By` nor "Generated with" in a commit or pull request: an `Assisted-by: <tool>:<model>`
  trailer, and the author answers for every line ([pull requests](CONTRIBUTING.md#pull-requests)).

## This project

<!--#if (!agentFilesExample)-->
Written by this project's team: what no page above holds. Replace each _to fill_ with it and delete
a line that does not apply; a line still marked _to fill_ is no rule yet.

- Domain and words: _to fill_, what the product does and what its terms mean.
- Systems it talks to: _to fill_, each outside service and who owns it.
- Leave alone: _to fill_, a folder or behaviour not to change, and why.
- Ask first: _to fill_, a change a person decides, such as a dependency or a public route.
- Departures: _to fill_, where this project departs from the pages above, and the ADR that says why.
<!--#endif-->
<!--#if (agentFilesExample)-->
Written by this project's team: what no page above holds. Each line still marked _Example_ is about a
made-up product and says nothing of this one: replace it, or delete the line.

- Domain and words: _Example_, climbing gyms rent wall slots; a slot is one wall for one hour.
- Systems it talks to: _Example_, the payment provider, owned by finance; the door controllers.
- Leave alone: _Example_, the pass pricing rules, set by contract with each gym.
- Ask first: _Example_, any new package, any change to a public route.
- Departures: _Example_, bookings keep a cancelled status; the ADR on cancelled slots says why.
<!--#endif-->
<!--#if (templateMaintenance)-->

## Working on the template

This repository is also the `dotnet new` template the project pages describe; pages under
`docs/template/` are about the template and never reach a generated project.

- `verify` also runs the template's gates: `tools/CheckDocPaths.cs`, `tools/CheckComments.cs`,
  `tools/CheckWorkflows.cs` and `dotnet run tools/WebLockfiles.cs --check`. Each runs alone
  with `dotnet run`; a generated project keeps none of them, and `DocumentationCitationTests` holds
  its citations.
- This repository runs the tool from its source: where a page says `dotnet cleanarch <command>`, run
  `dotnet run --project launcher/CleanArch.Cli -- <command>` here.
- A shared file conditions a sentence on an option only where the option changes what is true, a
  whole sentence per branch; read it in the default form and in
  `--auth none --examples none --files false`.
- Generate outside this repository, and remove every git worktree nested in the checkout before
  installing from it: the install reads each one as a second copy of the template.
- [Maintaining the template](docs/template/maintaining-the-template.md) holds the marker rules, the
  gates and the acceptance matrix.
<!--#endif-->
