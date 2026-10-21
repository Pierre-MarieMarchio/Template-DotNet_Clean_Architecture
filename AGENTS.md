# Agents

## This project

A .NET HTTP API on Clean Architecture. [The documentation index](docs/README.md) lists every page.

## Where truths live

When two sources disagree, the first wins, and the disagreement is a defect to fix in the same change:

1. The tests and the code. `Tests/Architecture` holds the structural rules.
2. The ADRs in `docs/decisions/`: why a choice was made.
3. The pages under `docs/`: how it works and how to use it.
4. The reference tables in `docs/reference/`: settings, routes, codes, folders.

## How the docs are organised

| Folder | Holds |
|---|---|
| `docs/getting-started/` | what a newcomer reads once, in order |
| `docs/architecture/` | the rules every layer, feature and endpoint follows |
| `docs/features/` | one capability per page, the product's own included |
| `docs/guides/` | a task repeated: add, remove, migrate, test, ship |
| `docs/reference/` | what is looked up, not read |
| `docs/decisions/` | one decision per file; the product's numbered from 1000 |
| `docs/_page-templates/` | the skeleton each kind of page starts from |

## Writing docs

- One question per page, answered from what the reader gets to the steps or the facts.
- Speak of "this project". No history, no plan vocabulary, no narration of how the code came to be.
- One idea per paragraph; the imperative for steps; tables for options and settings; one short sample
  per concept.
- The reason for a choice lives in an ADR, and the page links to it in one line. A decision is an ADR
  when another team could reasonably have taken it otherwise, a future maintainer must know it before
  changing it, and the code does not make it obvious; [decisions](docs/decisions/README.md) holds the
  criteria.
- Every path written in backticks exists. Write an illustration as a placeholder (`<Feature>`) or as
  prose.
- A new setting gets its row in [the configuration reference](docs/reference/configuration.md); a test
  requires it.
- Markdown files are UTF-8 with BOM and LF line endings.

## Changing code

- Code carries no explanatory comment. A name that needs one is renamed; a reason goes to an ADR or a
  page. Only OpenAPI XML documentation and one guidance line per configuration setting remain.
- Extract an abstraction only once two real cases prove its shape.
- A rule checked by hand becomes a test in `Tests/Architecture`; a decision is an ADR and the test
  that holds it, changed together.
- Run the gate before calling a change done: `dotnet run Tools/Tasks.cs verify`, then
  `dotnet run Tools/Tasks.cs coverage`. Both pass without overrides.

## Adding or removing a feature

<!--#if (canAddFeature)-->
`dotnet run Tools/Tasks.cs new-feature <Features> <Feature> --access owner|shared` writes the skeleton
and its page under `docs/features/`, and prints the edits to make by hand and the row to add to
`docs/README.md`. `remove-feature <Features>` reports what removing one involves and deletes an
untouched skeleton with its page. [Adding a feature](docs/guides/adding-a-feature.md) and
[removing features](docs/guides/removing-features.md) give the steps.
<!--#endif-->
<!--#if (!canAddFeature)-->
This project authenticates nobody, so `new-feature` refuses it; write a feature by hand from
[adding a feature](docs/guides/adding-a-feature.md), with its page from `docs/_page-templates/feature.md`
and its row in `docs/README.md`. [Removing features](docs/guides/removing-features.md) gives the
reverse.
<!--#endif-->
<!--#if (templateMaintenance)-->

## Working on the template

This repository is also the `dotnet new` template the project pages describe. Pages under
`docs/template/` are about the template and never reach a generated project. A project page
conditions a sentence, a row or a section on a generation option only where the option changes what
is true, a whole sentence per branch, and is read in every generated form;
[maintaining the template](docs/template/maintaining-the-template.md) holds the marker rules, the
template's own gates and the acceptance matrix.
<!--#endif-->
