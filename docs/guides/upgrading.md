---
type: guide
title: Upgrading
description: How to see what a newer version of the package that generated this project changes here before taking any of it.
---

# Upgrading

## When

A newer version of `CleanArch.WebApi.Templates`, the package that generated this project, is out, and
you want to know what it changes here before taking any of it. `upgrade-report` answers: it
generates this project twice, from the version `cleanarch.json` records and from the newer one, and
compares both generations with the files here. It writes nothing in the project; what to take stays
your decision, file by file.

## Steps

1. Read the report:

   ```bash
   dotnet run Tools/Tasks.cs upgrade-report --keep <folder>                  # the latest stable version
   dotnet run Tools/Tasks.cs upgrade-report --keep <folder> --to <version>   # a given one, a prerelease included
   ```

   It installs the package at both versions from the NuGet sources the NuGet configuration of this
   project and of the machine names, each in a template hive of its own under the system's
   temporary folder, which it deletes when it ends. Only the package's name and its versions reach
   a source: no file of the project leaves the machine. `--source <path>` reads a package file, or a
   folder of them, instead of the sources. `--keep <folder>` keeps the two generations, outside the
   project, as `<folder>/<version>`; `--json <file>` writes the report there as JSON as well.
2. Read the changelog entries the report quotes, those between the version here and the newer one: a
   minor or patch version is taken file by file, and a major version changes something whose adoption
   takes work by hand, which its entry says.
3. Take the decisions it lists first: an option one version has and the other has not, a value or a
   default that changed. A renamed option reads as one removed and one added: the report never
   assumes that two options are the same.
4. Copy each file listed under **Take** from `<folder>/<newer version>/<path>`: the newer version
   changed it, and this project did not.
5. Merge each file listed under **Merge** by hand, from the diff the report closes with:

   ```bash
   git merge-file <path> <folder>/<project version>/<path> <folder>/<newer version>/<path>
   ```

   merges it three ways and marks each conflict. A note says why a file is there when this project
   did not change it itself: bootstrap or a scaffold edits it, or the join of a part added since.
6. Add each file listed under **Added**, and delete each one under **Removed**, unless the note says
   this project made it its own.
7. Read **Changed scaffold descriptors**: the framework's CLI now creates that part differently,
   and the part here was created already, so take what the new descriptor brings by hand.
8. Set `template.version` in `cleanarch.json` to the newer version, then pin the project's tool at
   that version with `dotnet tool update CleanArch.Cli --local --version <newer version>`.
9. Run `dotnet run Tools/Tasks.cs format-fix`, which orders the using directives as bootstrap did,
   then `dotnet run Tools/Tasks.cs verify`.

## What the report compares

- **Files bootstrap or a framework's CLI writes are not compared.** The migrations, and wherever
  this project has them, an OpenAPI reference, the clients written from it, a web lockfile and the
  files a scaffold creates from its descriptor, exist in no raw generation. The report lists
  them apart, with the command that writes them. A file bootstrap edits, such as `.config/dotnet-tools.json`,
  is never offered to take as is.
- **What is not a difference.** Line endings and a leading byte order mark, which
  `.gitattributes` and the editor settle; the order of a C# file's using directives, which bootstrap
  sets; the GUIDs `dotnet new` draws for a solution's entries, read as the entries they name; and
  the ones it draws for the `guids` a template declares, such as each host's `UserSecretsId`: a GUID
  neither package holds is read as drawn, so one the template writes as is still counts.
- **Parts added since.** The generation takes the hosts under the names it gives them and the first
  web app; every other app, package and host replays its item template, in the order
  `cleanarch.json` records, with the defaults of what the file does not record: an app is replayed
  on its recorded `port`, or on its framework's default when the file records none, and an app
  recorded without one is a file to complete by hand, with the number its
  `Src/Web/apps/<App>/compose.yml` gives `WEB_<NAME>_HOST_PORT`, as `ProjectFileTests` asks. Their
  joins are not replayed, so a file a join edits reads under Merge, with a note.
- **Nothing from a package runs.** A version whose templates declare a post-action beyond printing
  instructions is refused, and so is a value `cleanarch.json` records that the newer version no
  longer offers: both leave the comparison to you.

A project without `cleanarch.json` has no record to regenerate from: generate a project from the
newer version under the same name and options in a folder of its own, and compare by hand.

## Check

`dotnet run Tools/Tasks.cs verify` passes, and `dotnet run Tools/Tasks.cs upgrade-report` says no
stable version is newer than the one `cleanarch.json` records.

## Undo

Nothing to undo: the report writes nothing in the project. What you took from it, git takes back.
