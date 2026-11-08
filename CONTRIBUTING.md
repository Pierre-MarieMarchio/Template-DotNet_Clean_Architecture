# Contributing

What has to be true before a change is done, and where each rule is written.

## The gate

A change is done when `dotnet cleanarch verify`, then `dotnet cleanarch coverage`,
pass without overrides; [build and CI](docs/guides/build-and-ci.md#the-gate) says what each step
checks, and [first run](docs/getting-started/first-run.md) what the machine needs.

## Rules and where they live

Each rule has one home, and a test holds it wherever a test can.

| Rule | Home |
|---|---|
| <!--#if (agentFiles)--> |
| How to work here: the commands, where code goes, what done means | [AGENTS.md](AGENTS.md) |
| <!--#endif--> |
| What may depend on what | [Architecture](docs/architecture/README.md), held by `tests/Architecture` |
| Which project and folder a file goes in, and its name | [Project layout](docs/reference/project-layout.md) |
| A feature's shape: an aggregate, or a service over a store | [Feature shapes](docs/architecture/feature-shapes.md) |
| <!--#if (api)--> |
| The contract every endpoint offers | [HTTP API](docs/architecture/http-api.md) |
| <!--#endif--> |
| <!--#if (agentFiles)--> |
| Clean comments: the code reads by itself; a comment only says what the code cannot | [AGENTS.md › Changing code](AGENTS.md#changing-code), [the stack](docs/architecture/README.md#the-stack) |
| Extract an abstraction only once two real cases prove it | [AGENTS.md › Changing code](AGENTS.md#changing-code) |
| <!--#endif--> |
| <!--#if (!agentFiles)--> |
| Clean comments: the code reads by itself; a comment only says what the code cannot | [the stack](docs/architecture/README.md#the-stack) |
| Extract an abstraction only once two real cases prove it | [Architecture › What is deliberately absent](docs/architecture/README.md#what-is-deliberately-absent) |
| <!--#endif--> |
| How tests are written | [Testing › House rules](docs/guides/testing.md#house-rules) |
| Adding and removing a feature | [Adding a feature](docs/guides/adding-a-feature.md), [Removing features](docs/guides/removing-features.md) |
| Migrations | [Migrations](docs/guides/migrations.md) |
| Every setting | [Configuration](docs/reference/configuration.md), held by `ConfigurationSurfaceTests` |
| <!--#if (agentFiles)--> |
| How a page is written, and which pages move with the code | [AGENTS.md › Writing docs](AGENTS.md#writing-docs), [AGENTS.md › Done means](AGENTS.md#done-means) |
| <!--#endif--> |
| <!--#if (!agentFiles)--> |
| How a page is written | [The documentation index](docs/README.md#adding-a-page) |
| <!--#endif--> |
| Why a choice was made | [Decisions](docs/decisions/README.md) |
| What a deployment still owes | [SECURITY.md](SECURITY.md) |

## Pull requests

- One concern per pull request: a refactor and a behaviour change in one diff cannot be reviewed.
- The gate passes, without overrides.
- New behaviour comes with a test you have seen fail: break the code, watch the test go red, restore
  it.
- A constraint you checked by hand becomes a rule in `tests/Architecture`.
- A choice goes on the page it concerns, with its reason; an ADR only as a last resort, as
  [when to write one](docs/decisions/README.md#when-to-write-one) says.
- Commits and pull requests name their human author only, with no `Co-Authored-By` or "Generated
  with" line naming an AI tool; the author answers for every line and can explain it. Where a tool
  wrote code, an `Assisted-by: <tool>:<model>` trailer and the pull request's Assistance section are
  recommended; bots such as Dependabot or release-please keep their own messages as they write them,
  emoji included, since a bot did write them.
- Each commit follows [the commit message form](#commit-messages).

## Commit messages

A commit message is read long after the diff is forgotten, so it says what the diff cannot: the
problem, and why this change solves it.

- **The subject** is `<type>(<scope>): <summary>`, in the imperative, 72 characters at most, with no
  final full stop. The type is one of [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/):
  `feat`, `fix`, `docs`, `refactor`, `test`, `build`, `ci`, `chore` and so on. The scope names the
  part touched and may be left out. A `!` after the type or scope, or a `BREAKING CHANGE:` footer,
  marks a change that breaks a caller.
- **The body** comes after a blank line and wraps at 72 characters. It describes the problem first,
  then why this change solves it, then what it does where the diff does not show it. A reader who
  stops after the first paragraph still knows why the commit exists.
- **One concern per commit**, as for a pull request: a refactor and a change of behaviour are two
  commits, and so is a rename and the feature that needed it.
- **Trailers** close the message, one per line, each short:
  - `Fixes: #<n>` or `Closes #<n>` for the issue the commit settles;
  - `Reported-by: <name>` for who found the problem;
  - `Link: <url>` for a discussion or a document the body cannot hold;
  - `Assisted-by: <tool>:<model>` where a tool wrote part of the change.
- **No `Co-Authored-By` naming an AI tool, and no "Generated with" line**: the author answers for
  every line, as [pull requests](#pull-requests) says. This project
  asks for no `Signed-off-by` line.

For example:

```text
fix(<scope>): reject an empty name when a feature is created

An empty name passed validation, so a record with no label reached the
store and showed as a blank row in every list.

The validator now requires one non-blank character, as the other
create commands already do.

Fixes: #<n>
Assisted-by: <tool>:<model>
```
<!--#if (templateMaintenance)-->

In this repository, release-please reads the types: a `feat` or a `fix` lands in the changelog, and a
`!` makes the next version a major one
([branches and releases](docs/template/maintaining-the-template.md#branches-and-releases)).

## Working on the template

This repository is also the template. A change starts from `develop` on a `<type>/<subject>` branch,
as a conventional commit, and returns to `develop` by a pull request; `main` holds the releases. The
branch is deleted once merged, since `develop` holds its commits and the pull request its discussion.
The branches and releases, its own gates, the acceptance matrix and the rules for writing pages that
condition on the generation options are in
[maintaining the template](docs/template/maintaining-the-template.md).
[The roadmap](docs/template/roadmap.md) says what is coming and in which order; a change it does not
list starts from an issue.

## Your first contribution

1. Pick an issue labelled `good first issue`; if none fits, ask in
   [Discussions](https://github.com/Pierre-MarieMarchio/cleanarch-dotnet/discussions) before writing
   code, so the change is agreed before it is made.
2. Branch from `develop`, as `<type>/<subject>`, and keep the change to one concern.
3. Run `dotnet cleanarch check` while you work, then
   [the gate](#the-gate) before you ask for a review.
4. Open the pull request against `develop` and fill in its template; the title is a conventional
   commit, as [the commit messages](#commit-messages) ask.
<!--#endif-->
