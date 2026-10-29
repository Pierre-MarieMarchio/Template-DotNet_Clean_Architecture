# 0014. A Windows-only shape is proved on a Windows runner

Status: Accepted

## Context

A team that works on Windows runs the generated project's tasks from PowerShell: `bootstrap`,
the build, the tests and `hygiene`. The acceptance matrix ran every leg on `ubuntu-latest`, in one
job of bash steps that start Linux containers, so no gate had run those tasks on Windows, where
paths, line endings and the processes a task starts differ. A shape that targets Windows alone, as
a WPF presentation does, restores and builds on Linux, but its tests and the tasks around it can
only run on Windows.

## Decision

- **A leg declares its runner.** A leg of `.template.config/acceptance/matrix.json` may set
  `runner` to `windows-latest`; it runs on `ubuntu-latest` otherwise.
  `.template.config/acceptance/SelectLegs.cs` lists the Windows legs to run apart from the others,
  the `template-windows` job runs them under the same check name, `Template acceptance (<leg>)`, and
  the `skipped` job reports them as it reports every leg the change cannot reach.
- **The Windows job runs what a Windows user runs.** From PowerShell, one command a step, it
  installs the package, generates, asserts the template's name leaked nowhere, runs `bootstrap`, the
  `build` task with warnings as errors, `test --no-integration`, `hygiene` and the citation check. It
  starts no container, since a Windows runner runs no Linux image, and sets up no web workspace.
- **The default shape has a Windows leg, and it is required.** `windows, defaults` carries no tag,
  so every change that runs every leg, one under `Src`, `Tests` or `Tools` among them, runs it too.
- **Tests hold it.** `AcceptanceSelectionTests` holds that each leg's runner is one a job serves and
  that a Windows leg sets only the fields the Windows job reads and generates no web app;
  `Tools/CheckWorkflows.cs` holds the new job to the complement rule of
  [0010](0010-a-pull-request-runs-the-legs-its-change-can-affect.md).

## Alternatives considered

- **One job whose runner comes from the matrix.** Every Docker and bash step of a long job would need
  a guard for the legs it cannot serve.
- **Bash steps on Windows.** Git Bash is on the runner, but a Windows user types the tasks into
  PowerShell; each step being one `dotnet` command, PowerShell costs nothing.
- **No Windows leg until a Windows-only shape arrives.** That shape's pull request would also be the
  first to run the tasks on Windows, and would carry their fixes.
- **A nightly Windows run.** A break would be found after the merge, by someone else, as
  [0010](0010-a-pull-request-runs-the-legs-its-change-can-affect.md) turned down.

## Consequences

- Windows runner minutes on every pull request whose change runs every leg.
- The Windows leg proves the default shape only; a Windows-only shape brings its own leg, on the same
  job.
- `Template acceptance (windows, defaults)` joins the required checks of the `develop` ruleset once
  the pull request that adds it merges.

## Revisit when

Windows runner time weighs on the pace of pull requests, or a Windows leg needs a step the Windows
job does not run.
