# 0021. A project reads a newer version by generating both

Status: Accepted

## Context

A generated project keeps every file it received, and its team changes them. When a newer version
of `CleanArch.WebApi.Templates` is out, the team needs to know which of its files that version
changes, which of those it changed too, and what the version adds, removes or decides differently.
The `dotnet new` engine offers no update. `cleanarch.json` records the version, the name, the
options and every part added since ([0018](0018-a-project-keeps-its-provenance-and-its-tool-from-the-template.md)),
which is everything a generation needs. Bootstrap and the frameworks' CLIs then write files no
generation holds, and an item template's join edits files outside its part.

## Decision

- **Generate both versions, then compare three ways.** `Tools/UpgradeReport.cs`, run as
  `upgrade-report`, installs the package at the project's version and at the target, each in a
  template hive of its own, generates the project from each under the recorded name and options,
  and replays the item templates of the parts the options cannot express, in the recorded order. A
  file the target changed is taken as is when the project's copy equals the first generation, and
  merged by hand otherwise.
- **It writes nothing in the project, and runs nothing from a package.** No `dotnet new` post-action
  beyond printing instructions is accepted. The parts' joins are not replayed: `dotnet run` would
  build them under the generated tree's MSBuild files, which come from the package, so a file a join
  edits reads as one to merge.
- **What no generation holds is listed, not compared.** The files bootstrap writes, the ones the
  scaffolds create, and the solution's GUIDs, which `dotnet new` draws anew each generation.
- **An option is never mapped.** One the two versions do not share, or a recorded value the target
  no longer offers, is a decision for the team.
- **The package carries its changelog** at its root, beside the README, so the report reads the
  entries between the two versions from the target package itself.

## Alternatives considered

- **Record the hash of each generated file**, as `.feature-manifest/` does for a feature. It tells
  what the project changed, not what the target changes, which still takes a generation of the
  target.
- **Apply the target as a three-way merge**, as Copier and Cruft do. It writes conflict markers into
  generated code and lockfiles, and the team reviews the same diffs afterwards, with the project
  already changed.
- **Diff the two packages' content.** The content is the checkout under its markers and tokens,
  unlike any generated project.

## Consequences

- A generation stays reproducible from `cleanarch.json`: an option the file does not record, or a
  generator that draws a value, shows every file it touches as changed. A web app's `--port` is not
  recorded, so an app created with another port reads as merged.
- `_writtenByBootstrap` and `_editedByBootstrap` in `Tools/UpgradeReport.cs` follow what bootstrap
  writes: a step added to bootstrap adds its paths there, or every project reads them as its own.
- The report replays an item template's options by the names its `template.json` declares; a
  renamed one is reported, not followed.
- `Tools/UpgradeReport.cs --self-test`, which `hygiene` runs, holds the classification, the
  exclusions, the options, the refusals and the changelog range.

## Revisit when

`dotnet new` gains an update of its own, or `dotnet cleanarch` takes the report over.
