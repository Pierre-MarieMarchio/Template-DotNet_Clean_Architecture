# 0017. A project runs its tasks from a pinned .NET tool

Status: Proposed

## Context

Every generated project receives `Tools/Tasks.cs` and what it runs: the generators
`Tools/NewFeature.cs`, `Tools/NewUseCase.cs` and `Tools/RemoveFeature.cs`, with
`Tools/CoverageGate.cs` and `Tools/TestSummary.cs`. That is about 11,600 lines, which the engine
conditions per option through `//#if` blocks. A fix to a generator reaches only the projects
generated after it: an older project keeps its copy, and its team keeps that copy in step by hand,
if it does at all. The copy is also what lets a team change a task, add one, or reshape the code a
generator writes. A tool installed from NuGet, the way `dotnet-ef` is, would take that away unless
it leaves room for it. The launcher `CleanArch.Create` already ships from this repository as a .NET
tool, under the template's version.

## Decision

- **The tasks and the generators ship as a .NET tool.** `CleanArch.Cli` is packed from this
  repository under the `templateVersion` of `.template.config/template.json`, as the launcher is.
  Its command is `dotnet-cleanarch`, run as `dotnet cleanarch <task>`, and its commands, help and
  completion come from System.CommandLine. A generated project pins it in
  `.config/dotnet-tools.json` beside `dotnet-ef`, and `bootstrap` restores it.
- **A project records what it was generated with.** A project file written at generation holds the
  project's name, its options and the template version. The tool reads it where `Tools/Tasks.cs`
  reads a `//#if` today, and the launcher reads it where it now infers an item's project facts from
  the project's files.
- **A team changes the tool at four depths**, each one costlier than the last:
  1. **Configuration.** The project file composes the tasks: what `verify` runs, and the steps
     that run before or after a task.
  2. **Code templates.** A generator writes from text templates embedded in the tool, and a
     template of the same name in the project wins. A command copies the built-in templates and
     records the hash of each copy's source, so the tool warns when a later version changes a
     template the project overrides, as `.feature-manifest/` does for a feature.
  3. **Local commands.** A file-based app `Tools/<name>.cs` runs as `dotnet cleanarch <name>`. A new
     name adds a command, a built-in name is replaced by it, and `help` says which is which.
  4. **Ejection.** `dotnet cleanarch eject <command>` copies a command's source into `Tools/`,
     where the third depth takes it over. That one command stops following the tool's releases.
- **The template's own gates stay here.** `Tools/CheckComments.cs`, `Tools/CheckDocPaths.cs` and
  `Tools/CheckWorkflows.cs` audit this repository and the generated trees, and the tool never
  carries them.
- **Tests hold it.** `TemplatePackagingTests` holds the pinned version to `templateVersion`, and
  the tool's own tests run each command against the project file of every leg of the acceptance
  matrix.

## Alternatives considered

- **Keep the copied tools.** Every team keeps a fork of 11,600 lines it did not write, and a fix
  never reaches a project already generated.
- **A tool with no room for change.** It is the lightest to maintain, but a team that needs another
  gate step or another endpoint shape has nowhere to put it except a fork of the tool.
- **Generators as `dotnet new` item templates.** An item template writes new files, but it cannot
  edit the registrations, the closed lists and the manifest that a feature joins.
- **A global tool.** Two projects on two template versions would share one version of the
  generators. A local tool pins each project's own.
- **Team plugins loaded as assemblies.** Loading a team's assemblies into the tool's process brings
  version and isolation problems that a file-based app, run in a process of its own, does not have.
- **One tool for creating and working.** The launcher reads the template's manifests once, before
  a project exists. Every project would otherwise carry that machinery for ever.

## Consequences

- A generated tree loses about 11,600 lines, and `Tools/` holds only what the team writes or ejects.
- The tool becomes a public interface. The project file's schema, the variables of the code
  templates and the contract of a local command are versioned with care, and breaking one of them
  takes a major version.
- Every page, every `AGENTS.md` and `DocumentationCitationTests` that cite `dotnet run Tools/Tasks.cs`
  move to `dotnet cleanarch`, and `.feature-manifest/` records the generator's version.
- The acceptance matrix tests one tool over each leg's project file instead of a conditioned copy
  per leg, and the generators gain unit tests of their own.
- A project now keeps in step with a package from the template, the condition under which
  [0002](0002-nothing-about-the-template-reaches-a-project.md) says to revisit it. Accepting this
  ADR comes with the ADR that supersedes 0002 and adds the tool to what reaches a project.

## Revisit when

The .NET SDK comes to offer project-scoped commands of its own, or teams come to eject most of the
commands.
