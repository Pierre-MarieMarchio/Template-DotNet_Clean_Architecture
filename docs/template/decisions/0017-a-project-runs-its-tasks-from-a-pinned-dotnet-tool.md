---
type: decision
title: 0017. A project runs its tasks from a pinned .NET tool
description: Why a project runs its tasks from a .NET tool pinned at the template's version, while another tool only creates projects.
status: Accepted
---

# 0017. A project runs its tasks from a pinned .NET tool

Status: Accepted

## Context

Every generated project received a task launcher script and what it ran: the generators
(`new-feature` and `new-field`, one more use case, a setting, an event and the removal of a
feature), the upgrade report, the coverage gate and the test summary, each a file-based program
under `tools/`. That is about 31,800 lines in a project of the
default options, which the engine conditions per option through `//#if` blocks. A fix to a
generator reaches only the projects generated after it: an older project keeps its copy, and its
team keeps that copy in step by hand, if it does at all. The copy is also what lets a team change a task, add one, or reshape the code a
generator writes. A tool installed from NuGet, the way `dotnet-ef` is, would take that away unless
it leaves room for it.

A project is worked on through three surfaces that overlap. The task launcher held 33 tasks in a
project of the default options. The launcher `CleanArch.Create`, a .NET tool packed from this
repository under the template's version, creates projects but also adds web apps, web packages and
presentation projects to an existing one.
The item templates write the files of those additions, and the IDE reaches them too. The launcher's
`presentation` command runs the same steps that `presentation-join` and `presentation-scaffold`
expose one by one.

## Decision

- **A tool to create, a tool to work, as Vite does.** `dnx CleanArch.Create` creates a project and
  nothing else, as `npm create vite` does. Everything done inside a project, including adding a web
  app, a web package or a presentation project, belongs to the project's own tool, as `vite` does,
  following [0019](0019-the-launcher-creates-and-the-project-tool-adds.md).
- **The project's tool ships as a .NET tool.** `CleanArch.Cli` is packed from this repository under
  the `templateVersion` of `.template.config/template.json`, as the launcher is. Its command is
  `dotnet-cleanarch`, run as `dotnet cleanarch <command>`, and its commands, help and completion
  come from System.CommandLine. A generated project holds it in `.config/dotnet-tools.json` beside
  `dotnet-ef` from the generation on, pinned at the template's version, so the first step is
  `dotnet tool restore`, then `dotnet cleanarch bootstrap`, which the launcher runs. `bootstrap` does
  not install the tool; it is the tool.
- **A project records what it was generated with.** A project file written at generation holds the
  project's name, its options, the template version and the parts added since, and the tool
  manifest is written at generation too. The tool finds the
  project root by this file and reads it where the task launcher read a `//#if`.
  `dotnet cleanarch adopt` writes it for a project generated before the tool, from the facts the
  launcher already infers from a project's files.
- **The project's facts come from the project file.** A question about the project is answered from
  it, never inferred from files a team edits; inference survives only in `adopt`. `add web-app`,
  `add web-package` and `add presentation` run the item template at the version the project file
  records, so an addition matches the project's template version whatever launcher a user runs.
- **Two things from the template reach a project, and only these two:** its provenance, the project
  file and one sentence of `docs/getting-started/overview.md` naming the package, the version and
  the options of the day of generation; and the tool, pinned in `.config/dotnet-tools.json` and
  moved to a later version only by the team's `dotnet tool update`. The tool's help and messages
  speak of "this project", as the pages do, and it never carries a template-maintenance gate.
- **Both tools ask from the templates' manifests.** Each visible option of `ide.host.json`, in its
  order and under its label, with the choices of `template.json` and the long names of
  `dotnetcli.host.json`, embedded from the commit the tool is packed from. A refusal is evaluated,
  never restated: the tool evaluates its `isRequired` on the answers and asks again the options it
  reads, and a question offers only what some answer to the rest can generate. The launcher and the
  tool share that code in `launcher/CleanArch.Tooling`.
- **The commands follow five rules:**
  1. Each command prints what it runs, so the printed line gives the same result without the tool.
  2. A command takes the names of the tool its users already know: `add` and `remove` as
     `dotnet add` has them, `migrations add` and `database update` as `dotnet ef` has them.
  3. A command that keeps a file in step takes `--check`, which fails where it would write instead.
     `verify` is the chain of those checks with the build and the tests.
  4. A generator takes `--dry-run`, and a command that deletes reports by default and writes only
     with `--apply`. `adopt` and `eject` create a file once rather than keep one in step, so they
     take `--dry-run` as a generator does, and no `--check`.
  5. `help` lists only what the project file makes true. A command that does not apply to the
     project is refused, with the reason.
- **Each task becomes a command or a step.** `new-feature`, `new-use-case`, `new-field`,
  `new-setting` and `new-event` become `add feature`, `add use-case`, `add field`, `add setting` and
  `add event`, each with `--dry-run`. `remove-feature`, which reports and writes only with
  `--apply`, becomes `remove feature`, with the same `--apply`. `add feature` and `add field` are
  refused where the project file records `auth: none`, as `new-feature` is, since every feature
  they write needs an authenticated caller. `upgrade-report` stays a command of
  its name, which writes nothing in the project, and `migration-check` becomes the `--check` of
  `migrations add`.
- **The internal steps are not commands.** Joining and scaffolding an addition are steps of
  `add web-app`, `add web-package` and `add presentation`. Each of these completes whatever is
  missing, so it also finishes an addition that the IDE started through an item template. The
  initial migrations are a step of `bootstrap`, and the repository checks are a step of `verify`.
- **A team changes the tool at four depths**, each one costlier than the last:
  1. **Configuration.** The project file adds steps to a command, before or after it, and adds
     steps to `verify`. Removing a step of `verify` takes a project ADR, which the project file
     names, and a test refuses the removal without it: `verify` is the gate the CI runs.
  2. **Code templates.** A generator writes the files it creates from a reference feature written
     as compilable C#, under placeholder names and with the template engine's conditions and name
     forms, and instantiates it through the template engine. A template of the same path in the
     project wins. `dotnet cleanarch templates copy <generator>` copies the built-in ones and
     records the hash of each copy's source, so the tool warns when a later version changes a
     template the project overrides, as `.feature-manifest/` does for a feature. The edits a
     generator makes to existing files are the tool's code, outside this depth.
  3. **Local commands.** A file-based app `tools/<name>.cs` runs as `dotnet cleanarch <name>`. A new
     name adds a command, a built-in name is replaced by it, and `help` says which is which. Only a
     name of lower-case words joined by hyphens is a command, so the scripts named in PascalCase
     stay scripts, and `help` stays the tool's, since it says which
     commands are the project's own.
  4. **Ejection.** `dotnet cleanarch eject <command>` copies a command's source into `tools/`,
     where the third depth takes it over. That one command stops following the tool's releases.
     `help`, `eject` and `adopt` are not ejected, since they say what the project's own commands
     are, make them, and run before the tool knows the project. Nor are `add` and `remove`: they
     run on CleanArch.Tooling, the library they share with the launcher, on Spectre.Console and on
     Roslyn, none of which a file-based app carries, and the code a generator writes changes
     through its code templates instead.
- **The template's own tasks and gates stay here.** `tools/CheckComments.cs`,
  `tools/CheckDocPaths.cs`, `tools/CheckWorkflows.cs` and the web lockfiles' task audit this
  repository and the generated trees, and the tool never carries them. This repository runs the tool
  from its source, and the acceptance matrix packs it into a local feed that each generated project
  restores from with `dotnet tool restore --add-source`.
- **Tests hold it.** `TemplatePackagingTests` holds the pinned version to `templateVersion`, and
  the tool's own tests run each command against the project file of every leg of the acceptance
  matrix.

## Alternatives considered

- **Keep the copied tools.** Every team keeps a fork of some 31,800 lines it did not write, and a
  fix never reaches a project already generated.
- **A tool with no room for change.** It is the lightest to maintain, but a team that needs another
  gate step or another endpoint shape has nowhere to put it except a fork of the tool.
- **A configuration that composes `verify` freely.** The word `verify` would then promise nothing
  from one project to the next, and the CI would run whatever the file says.
- **Generators as `dotnet new` item templates alone.** An item template writes new files, but it
  cannot edit the registrations, the closed lists and the manifest that a feature joins.
- **Code templates in Scriban or Liquid.** They are lighter, but their files do not compile, the IDE
  does not help with them, and the repository would carry a second templating language.
- **Code kept as strings in the generators**, as the file-based generator kept it. A team could
  change it only by ejecting the whole command.
- **A global tool.** Two projects on two template versions would share one version of the
  generators. A local tool pins each project's own.
- **Team plugins loaded as assemblies.** Loading a team's assemblies into the tool's process brings
  version and isolation problems that a file-based app, run in a process of its own, does not have.
- **The additions left in the launcher.** Two tools would add to a project, `dnx` would run the
  latest launcher against a project pinned to an older template, and the facts would keep being
  inferred from files a team edits.
- **The options, or the refusals, written into the tools.** They would drift from the templates at
  the first option added, and a user could answer into a combination the engine refuses.
- **One tool for creating and working**, the way `ng` does it. Every project would carry for ever
  the machinery that asks a new project's questions, which runs once, before the project exists.

## Consequences

- A generated tree loses about 31,800 lines, and `tools/` holds only what the team writes or ejects.
- About 25 commands replace the 33 tasks: the joins, the scaffolds, the initial migrations, the
  migration check, the restore and the repository checks stop being commands of their own, `format`
  and `format-fix` become `format` and its `--check`, and `openapi` writes the clients after the
  reference, in place of `api-client`: the .NET client where the project holds one, and the web
  workspace's schema through `pnpm api <reference>` where it holds the `api-client` package. A
  project with the API and no .NET client still gets its reference.
- The launcher and the tool share their process, terminal and manifest code in one solution.
- A refusal keeps its shape, a hidden parameter whose only choice is `refused` with a conditional
  `isRequired`, and `refusedOptions` stays its union: the launcher's tests fail on every combination
  where the two disagree.
- The tool becomes a public interface. The project file's schema, the code templates and the
  contract of a local command are versioned with care, and breaking one of them takes a major
  version.
- Every page, every `AGENTS.md`, every workflow and `DocumentationCitationTests` that cite
  the task launcher move to `dotnet cleanarch`, and `.feature-manifest/` records the
  generator's version.
- The acceptance matrix tests one tool over each leg's project file instead of a conditioned copy
  per leg, and the generators gain unit tests of their own.
- A project keeps in step with a package from the template, the condition under which
  [0002](0002-nothing-about-the-template-reaches-a-project.md) says to revisit it. This ADR is
  accepted together with [0018](0018-a-project-keeps-its-provenance-and-its-tool-from-the-template.md),
  which supersedes 0002 and adds the tool to what reaches a project, and with 0019.

## Revisit when

The .NET SDK comes to offer project-scoped commands of its own, teams come to eject most of the
commands, the template engine comes to run post-actions that edit a project's existing files, or it
exposes a template's parameters and constraints through an API a tool can call.
