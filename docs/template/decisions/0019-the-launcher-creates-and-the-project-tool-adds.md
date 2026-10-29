# 0019. The launcher creates, and the project's tool adds

Status: Proposed; once accepted with [0017](0017-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md), replaces the project-facts bullet of [0004](0004-the-launcher-reads-the-templates-manifests.md)

## Context

[0004](0004-the-launcher-reads-the-templates-manifests.md) has `dnx CleanArch.Create` ask the
questions of `cleanarch-webapi`, `cleanarch-webapp`, `cleanarch-webpackage` and
`cleanarch-presentation`. For the three item templates, it infers the project's facts from the
project's files: its name, its authentication, its example features, its Files module, its
database, its workflows, its agent files, its scope and its workspace. Its `web-app`, `web-package`
and `presentation` commands then work inside a project that [0017](0017-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md)
gives a tool of its own, pinned at the project's template version, with a project file that records
those facts.

## Decision

- **The launcher creates a project and nothing else.** `dnx CleanArch.Create` keeps its `new`
  command and its questions about `cleanarch-webapi`. It ends by running `dotnet cleanarch
  bootstrap` in the project it created.
- **Additions belong to the project's tool.** `dotnet cleanarch add web-app`, `add web-package` and
  `add presentation` ask the item template's questions, run the item template at the version the
  project file records, make the edits its README lists, create its pages, and record the addition
  in the project file. Run where the item template's files are already present, they complete the
  rest.
- **The facts come from the project file.** A question about the project is answered from the
  project file, never inferred from the project's files. Inference survives only in
  `dotnet cleanarch adopt`, which writes the project file of a project generated before the tool.
- **The questions keep 0004's rules.** They are read from the manifests of the tool's own version,
  a refusal is evaluated rather than restated, and a question offers only what some answer to the
  rest can generate. The launcher and the tool share that code.

## Alternatives considered

- **Leave the additions in the launcher.** Two tools would then add to a project, `dnx` would run
  the latest launcher against a project pinned to an older template, and the facts would keep being
  inferred from files a team edits.
- **One tool for creating and working.** [0017](0017-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md)
  turns it down: every project would carry the creation questions for ever.

## Consequences

- An addition always matches the project's template version, whatever version of the launcher a
  user runs.
- The launcher's tests of the item templates' questions move to the tool. Its tests of the
  creation questions stay, still reading the manifests as 0004 says.
- A user who adds a part with `dotnet new` or the IDE then runs the matching `add` command, which
  completes the edits and records the part.

## Revisit when

The template engine comes to run post-actions that edit a project's existing files, or a project
needs to add a part that has no item template.
