# Contributing

This file is the working agreement: what has to be true before a change is done, and where each
rule is written down. It is short on etiquette and long on pointers, because the rules themselves
live next to the subject they are about.

## Getting a green build

```bash
dotnet restore AppTemplate.sln
dotnet build   AppTemplate.sln
dotnet test    AppTemplate.sln          # needs Docker for the integration suites
```

Prerequisites are the .NET SDK version pinned in `global.json` — nothing else, because everything
under `Tools/` is a file-based C# app — and Docker.
[`docs/BUILD-AND-CI.md`](docs/BUILD-AND-CI.md) is the full picture: the task launcher, every gate,
and what CI runs.

## The gate

A change is done when this passes, without overrides:

```bash
dotnet run Tools/Tasks.cs verify
```

That is the whole gate in CI's order — the four hygiene gates each after their own self-test,
restore, formatting, build with warnings as errors, the tests, and migration drift for both
contexts. The coverage floor is `dotnet run Tools/Tasks.cs coverage`, and
`dotnet list package --vulnerable --include-transitive` has to report nothing.

Three things about it are worth knowing before they cost you time.

**`TreatWarningsAsErrors` is not negotiable.** No `#pragma warning disable`, no `<NoWarn>`, no
lowering a severity in `.editorconfig`. A diagnostic genuinely wrong for one place gets a
path-scoped `.editorconfig` section with a comment saying why.

**`.cs` and `.md` files are UTF-8 with BOM**, and a file created without one fails the formatting
gate on encoding alone, with a message that does not say so. `dotnet run Tools/Tasks.cs format-fix`
after creating or moving any file.

**A gate proves it can go red before it judges anything.** Adding a rule to one of the four hygiene
gates means adding the fixture that fails without it.

**Sonar is not one of the gates, and it can still fail your pull request.** The gate above is what a
clone can tell you before you push; the quality gate judges new code and blocks the check. Both are
in [`docs/BUILD-AND-CI.md`](docs/BUILD-AND-CI.md).

## Where the rules live

Nothing below is restated here. Each document is the single statement of its subject, and where a
test can hold a rule it does — a rule nothing verifies is re-derived and lost inside six months.

| Subject | Document |
|---|---|
| Which project and which folder a file goes in | [`docs/PROJECT-LAYOUT.md`](docs/PROJECT-LAYOUT.md) |
| Naming, visibility, where a contract lives, what a comment may say | [`docs/CONVENTIONS.md`](docs/CONVENTIONS.md) |
| Why the layers are drawn this way, and what is deliberately absent | [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) |
| The choices a reasonable person could have made differently | [`docs/DECISIONS.md`](docs/DECISIONS.md) |
| Adding a feature, end to end | [`docs/ADDING-A-FEATURE.md`](docs/ADDING-A-FEATURE.md) |
| Removing one | [`docs/REMOVING-THE-EXAMPLE-FEATURES.md`](docs/REMOVING-THE-EXAMPLE-FEATURES.md) |
| The test stack, the house rules, and the sharp edges | [`docs/TESTING.md`](docs/TESTING.md) |
| Migrations, for both contexts | [`docs/MIGRATIONS.md`](docs/MIGRATIONS.md) |
| Configuration keys | [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md) |
| What a deployment still owes | [`SECURITY.md`](SECURITY.md) |

[`docs/README.md`](docs/README.md) is the index over all of them.

## Adding a feature

The vertical, from the inside out: aggregate → EF model → mapper → tracker → repository → use case
with its named interface → controller → tests → migration.
[`docs/ADDING-A-FEATURE.md`](docs/ADDING-A-FEATURE.md) walks it with the real signatures at each
step and names the architecture rule that reads each one.

```bash
dotnet run Tools/Tasks.cs new-feature Widgets Widget   # the folder is plural, the type is singular
```

The scaffolder writes the shape so the document can be about the thinking. Two things it cannot do
for you: **every step is enforced by a test rather than by the compiler**, so run
`dotnet test Tests/Architecture` before pushing; and **registration is opt-in per feature**, so a
feature nobody composes is registered nowhere and a domain-event consumer bound by nobody compiles
and never runs.

`TodoLists` is the worked example. `Reminders` is the more useful comparison for a new feature — a
flat aggregate with no child entities, so what a to-do list needs *only* because it owns items is
visible by what a reminder does without.

## Documentation is part of the change

Two gates read the documentation, and they are not proofreading.

`Tools/CheckDocPaths.cs` checks every repository path cited in a Markdown code span against the
filesystem, and every `docs/` path cited by a comment in a manifest, a script or a `.http` file. A
wrong path in a template's documentation costs the reader an hour. **An example path that names no
real file has to be written so it does not read as one** — a placeholder such as `<Your.Name>.sln`
rather than a plausible-looking name.

`Tools/CheckNarrativeComments.cs` holds the comment rule over `.cs` and `.md` alike, `CHANGELOG.md`
excepted — narrating history is what a changelog is for. **Rationale belongs in `docs/`**, not in a
paragraph above a method: measurements, rejected options and arbitrations go in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/DECISIONS.md`](docs/DECISIONS.md) or
[`docs/plans/`](docs/plans/).

So: a change that moves a file updates the document that cites it, and a new architectural
constraint becomes a section in the document that owns the subject *and* an executable rule in
`Tests/Architecture/`.

## Pull requests

- **One concern per PR.** A refactor and a behaviour change in one diff cannot be reviewed.
- **The gate passes, without overrides.**
- **New behaviour comes with a test you have seen fail.** For anything about security or
  correctness: break the production code, watch the test go red, restore it, and check the failure
  named what you thought it named. A green test you never saw fail is not evidence.
- **A new architectural constraint you checked by hand becomes an executable rule** in
  `Tests/Architecture/`, or it will be re-derived and lost.
- **A decision that could reasonably have gone the other way is written down** in
  [`docs/DECISIONS.md`](docs/DECISIONS.md), with the test that holds it.
