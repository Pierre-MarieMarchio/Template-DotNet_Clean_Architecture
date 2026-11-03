# 0031. The feature generator composes what it writes

Status: Accepted

## Context

A feature is not done when its files exist: it composes through lines in files it does not own, the
context, the modules, each host's `Program.cs`, the test compositions and the reference pages. A
generator that writes the files and prints those lines leaves a person to copy a dozen edits, in
order, from a terminal, and an edit copied wrong is a feature that half composes. The edits are
plain lines placed beside lines the shared files already hold, so a program can make them; what it
must not do is make some of them and stop, nor fold a model change that is not the feature's into the
feature's migration.

## Decision

- **`new-feature` makes the edits it would print**: each is placed beside a line it finds in the
  shared file, with the usings it needs, and the feature's rows join the reference pages.
  `new-use-case` does the same for its action, its contracts and its route's row.
- **Every edit or none**: every edit is computed in memory and every line it is placed beside found
  before a file is written, the feature's own files included; a missing line writes nothing, prints
  the edits to make by hand and names the one that failed.
- **An edit already in place counts as made**, so it is never written twice, and a run repeated with
  the same options writes nothing and resumes the commands below.
- **The manifest records each edit**: the file, the line it is placed beside, before or after it,
  and the lines added, so an edit can be undone exactly; an edit found in place is not recorded.
- **Before writing**, the command refuses while a context has no initial migration or the business
  model holds a change no migration holds (`migration-check`). **After writing**, it builds, adds the
  `Add<Features>` migration, then rewrites the OpenAPI reference and what is written from it, and
  stops at the first failure, naming what is done and the commands that remain.
- **One build per run**: the steps after the build pass `--no-build` and work from it. The check
  before writing builds what `dotnet ef` needs of the tree as it stands, since the build after
  writing cannot serve a check that must pass first.
- `--dry-run` is the only way to write nothing; nothing skips the migration.
- `dotnet run Tools/NewFeature.cs --self-test` and `dotnet run Tools/NewUseCase.cs --self-test`,
  which `hygiene` and `verify` run, hold each face.

## Alternatives considered

- **Printing the edits for a person to make**: no file outside the feature changes without a person,
  and a dozen lines copied by hand, in order, is where a feature goes wrong; the printed form also
  needed a second program to prove it in the acceptance.
- **Discovering the features by reflection or a source generator** instead of registering them:
  nothing to edit, and a composition nobody can read in `Program.cs`, which every host of this
  project states line by line.
- **Rewriting the files through their syntax tree**: exact for C#, and no help for the Markdown
  tables, for a file a team reshaped, or for a reader who wants to see the line an edit goes beside.
- **Making the edits that can be made and printing the rest**: a tree half composed, which neither
  builds nor says which half is missing.
- **Adding the migration later, by hand**: the feature's schema then depends on a step a person can
  forget, and a model change waiting in the tree would land in whichever migration came next.

## Consequences

- A generated feature builds, composes and has its migration in one command; the shared files keep
  the lines the edits are placed beside, and a team that renames one of them gets a refusal naming the
  edit, never a half-written tree.
- A feature written this way is registered, so `remove-feature --apply` refuses it until its edits,
  which its manifest lists, are undone.
- The command needs what `build`, `migration-add` and `openapi` need, the restored tools among them,
  and takes as long as one build and the steps that read it.
- Changing a shared file's anchor line changes the generator: its self-test names the anchors.

## Revisit when

The shared files stop holding a stable line to place an edit beside, or the composition moves to a
form that needs no edit.
