# Agents

How to work in this repository: the commands, where code goes, the rules, and what done means.
<!--#if (localAuth)-->
This project is a .NET 10 HTTP API on Clean Architecture whose callers sign in with local accounts.
<!--#endif-->
<!--#if (externalAuth)-->
This project is a .NET 10 HTTP API on Clean Architecture whose callers bring an OpenID Connect token.
<!--#endif-->
<!--#if (publicApi)-->
This project is a .NET 10 HTTP API on Clean Architecture that authenticates nobody.
<!--#endif-->

Your request comes first, then the `AGENTS.md` nearest the file you change, then this one. A folder
holding its own `AGENTS.md` adds what that folder needs: read it before changing a file there.
<!--#if (anyWeb)-->
`Src/Web/AGENTS.md` is the one for the web workspace.
<!--#endif-->
Read [the overview](docs/getting-started/overview.md) and [the architecture](docs/architecture/README.md)
once before a change that adds a type; [the documentation index](docs/README.md) lists every page.

## Commands

From the repository root. Right after generating, before anything else, run `bootstrap` and commit
what it writes: it formats the usings the rename reordered, writes the OpenAPI reference, restores
the local tools and creates each context's initial migration, and the other tasks assume all four.

| When | Command | Needs |
|---|---|---|
| Once, right after generating | `dotnet run Tools/Tasks.cs bootstrap` | the network |
| While you work | `dotnet run Tools/Tasks.cs test --no-integration` | nothing |
| The structural rules alone | `dotnet test --project Tests/Architecture/AppTemplate.Architecture.Tests` | nothing |
| After creating or moving a file | `dotnet run Tools/Tasks.cs format-fix` (commit apart what it rewrites in files you did not touch) | nothing |
| After changing the model | `dotnet run Tools/Tasks.cs migration-add <Name>` | the initial migration bootstrap creates |
| Every task and option | `dotnet run Tools/Tasks.cs help`; `-m:<N>` caps build and test parallelism | nothing |
| Before calling a change done | `dotnet run Tools/Tasks.cs verify`, then `dotnet run Tools/Tasks.cs coverage` | Docker, the tools bootstrap restores |
| After changing the stack, a mail or a relay | `dotnet run Tools/Tasks.cs end-to-end` | Docker |

## Where code goes

- [Project layout](docs/reference/project-layout.md): which project and folder a file goes in, and its
  name.
- [Feature shapes](docs/architecture/feature-shapes.md): an aggregate, or a service over a store;
  choose the aggregate when use cases beyond the five operations are foreseen (`new-use-case`
  refuses a service).
- [Modules and boundaries](docs/architecture/modules-and-boundaries.md): what may depend on what.
- [HTTP API](docs/architecture/http-api.md): the contract every endpoint offers.
- A registration, an anonymous endpoint, a folder word, a domain event or a new project joins a list a
  test reads; [the closed lists](docs/guides/adding-a-feature.md#the-closed-lists-a-feature-joins)
  name each one and the test that fails until it is there.

## Changing code

- Clean comments: the code reads by itself, through its names and its shape. A comment stays only
  when it says what the code cannot: a reason, a constraint from outside, a reference. A comment that
  explains what the code does means the code needs reworking: rename, extract or simplify it. A
  reason that outlives a line goes to an ADR or a page.
- Never silence a diagnostic with `#pragma`, `<NoWarn>`, a lowered severity or a skipped test: fix the
  code, or ask. [The gate](docs/guides/build-and-ci.md#the-gate) says why.
- Extract an abstraction only once two real cases prove its shape.
- A new setting is an options class with its `IValidateOptions`, bound with `ValidateOnStart`, and
  gets its own section in [the configuration reference](docs/reference/configuration.md);
  `ConfigurationSurfaceTests` fails until it has one. [Adding a setting](docs/guides/adding-a-setting.md)
  has the steps.
- A test follows [the house rules](docs/guides/testing.md#house-rules), and a test that buys a
  guarantee is seen failing once: break the code, watch it go red, restore it.
<!--#if (accounts)-->
- Another caller's record answers `404`, never `403`: the owner is in the query
  ([ADR 0009](docs/decisions/0009-ownership-is-in-the-query.md)).
<!--#endif-->
- A rule checked by hand becomes a test in `Tests/Architecture`; a decision is an ADR and the test
  that holds it, changed together.

## Files not edited by hand

<!--#if (canAddFeature)-->
- `.feature-manifest/`: `remove-feature` compares its hashes to know what is untouched.
<!--#endif-->
- A migration: `dotnet ef` writes it, bootstrap the first; one a database has applied is never
  rewritten, as [migrations](docs/guides/migrations.md) says.
- `coverage.minimum`: never lowered to pass; re-measured as [testing](docs/guides/testing.md#coverage)
  says.

## Writing docs

- One question per page, in the voice of "this project": no history, no plan vocabulary.
- A new page starts from `docs/_page-templates/` and gets its row in
  [the documentation index](docs/README.md#adding-a-page).
- A choice another team could reasonably have made otherwise is an ADR;
  [decisions](docs/decisions/README.md) holds the criteria, and the page links to it in one line.
- Every path in backticks exists; write an illustration as a placeholder (`<Feature>`) or as prose.
  `DocumentationCitationTests` checks the root pages, the `AGENTS.md` files and every task a page cites.
- Markdown and C# files are UTF-8 with BOM and LF line endings; `format-fix` repairs them.

## Adding or removing a feature

<!--#if (canAddFeature)-->
```bash
dotnet run Tools/Tasks.cs new-feature <Features> <Feature> --access owner --deletion delete|anonymise
dotnet run Tools/Tasks.cs new-feature <Features> <Feature> --access shared
dotnet run Tools/Tasks.cs new-use-case <Features> <UseCase> --kind command|query
dotnet run Tools/Tasks.cs remove-feature <Features>           # reports, writes nothing
dotnet run Tools/Tasks.cs remove-feature <Features> --apply   # deletes an untouched skeleton
```

`new-feature` writes the skeleton, its manifest and its page, then prints the hand edits, the
`docs/README.md` row and the `docs/reference/api.md` row; its fields (`Name`, `Description`) are
placeholders you rewrite. `new-use-case` adds one operation to a rich feature (a simple one is
promoted first) and prints its action.
[Adding a feature](docs/guides/adding-a-feature.md) and [removing features](docs/guides/removing-features.md) give the steps.
<!--#endif-->
<!--#if (!canAddFeature)-->
This project authenticates nobody, so `new-feature` refuses it; write a feature by hand from
[adding a feature](docs/guides/adding-a-feature.md), with its page from `docs/_page-templates/feature.md`
and its row in `docs/README.md`. `new-use-case` then adds one operation to it once it has the rich
shape. [Removing features](docs/guides/removing-features.md) gives the reverse.
<!--#endif-->

## Done means

- `verify`, then `coverage`, pass without overrides.
<!--#if (ci)-->
  `.github/workflows/ci.yml` runs the same gate on every pull request.
<!--#endif-->
- Without Docker, or when `verify` or `coverage` stops on the machine rather than on a test, run what
  can run (`test --no-integration`, the failing step's command by hand) and say which gate did not
  run and why; a change is never done on a build alone.
- The report names what ran, with its result, and what did not run. A change that compiles is not a
  guarantee proved: say which test holds it.
- The docs move with the code: a page that cites a moved file, the page of a new capability, the
  configuration row of a new setting, the ADR of a new choice.
- One concern per change, as [pull requests](CONTRIBUTING.md#pull-requests) asks.

## Where truths live

When two sources disagree, the first wins, and the disagreement is a defect to fix in the same change:

1. The tests and the code. `Tests/Architecture` holds the structural rules.
2. The ADRs in `docs/decisions/`: why a choice was made.
3. The pages under `docs/`: how it works and how to use it.
4. The reference tables in `docs/reference/`: settings, routes, codes, folders.
<!--#if (templateMaintenance)-->

## Working on the template

This repository is also the `dotnet new` template the project pages describe; pages under
`docs/template/` are about the template and never reach a generated project.

- `verify` also runs the template's gates: `Tools/CheckDocPaths.cs`, `Tools/CheckComments.cs`,
  `Tools/CheckWorkflows.cs` and `dotnet run Tools/Tasks.cs web-lockfiles --check`. Each runs alone
  with `dotnet run`; a generated project keeps none of them, and `DocumentationCitationTests` holds
  its citations.
- A shared file conditions a sentence on an option only where the option changes what is true, a
  whole sentence per branch; read it in the default form and in
  `--auth none --examples none --files false`.
- Generate outside this repository, and remove every git worktree nested in the checkout before
  installing from it: the install reads each one as a second copy of the template.
- [Maintaining the template](docs/template/maintaining-the-template.md) holds the marker rules, the
  gates and the acceptance matrix.
<!--#endif-->
