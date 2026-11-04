# 0014. A Windows-only shape is proved on a Windows runner

Status: Accepted

## Context

A team that works on Windows runs the generated project's tasks from PowerShell: `bootstrap`,
the build, the tests and `hygiene`. On Windows, paths, line endings and the processes a task
starts differ from Linux, so a run on `ubuntu-latest` proves none of them there. The acceptance
matrix's legs run on `ubuntu-latest` unless they say otherwise, in one job of bash steps that start
Linux containers, which a Windows runner cannot run. A shape that targets Windows alone, as a WPF
presentation does, restores and builds on Linux, but its tests and the tasks around it run only on
Windows.

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
- **The default shape runs on Windows with WPF, and it is required.** `windows, wpf, local auth`
  generates with `--presentation wpf` alone, which keeps every other default and adds the desktop
  app, so one leg proves the default project's tasks and the Windows-only shape; every change that
  runs every leg, one under `Src`, `Tests` or `Tools` among them, runs it too, and so does a change
  to the WPF starter. `windows, wpf, external auth, notes` proves the app under the other sign-in.
- **Tests hold it.** `AcceptanceSelectionTests` holds that each leg's runner is one a job serves and
  that a Windows leg sets only the fields the Windows job reads and generates no web app, and that
  one Windows leg passes `--presentation wpf` and nothing else;
  `Tools/CheckWorkflows.cs` holds the `template-windows` job to the complement rule of
  [0010](0010-a-pull-request-runs-the-legs-its-change-can-affect.md).

## Alternatives considered

- **One job whose runner comes from the matrix.** Every Docker and bash step of a long job would need
  a guard for the legs it cannot serve.
- **Bash steps on Windows.** Git Bash is on the runner, but a Windows user types the tasks into
  PowerShell; each step being one `dotnet` command, PowerShell costs nothing.
- **No Windows leg unless a shape needs one.** The tasks would stay unproved on Windows until a
  Windows-only shape arrives, and its pull request would carry their fixes.
- **A Windows leg for the default shape apart from the WPF one.** It runs the same tasks on a project
  the WPF leg holds whole, for Windows runner minutes the WPF leg already spends.
- **A nightly Windows run.** A break would be found after the merge, by someone else, as
  [0010](0010-a-pull-request-runs-the-legs-its-change-can-affect.md) turns down.

## Consequences

- Windows runner minutes on every pull request whose change runs every leg.
- A Windows-only shape brings its own leg, on the same job, or joins the default shape's when it
  keeps every other default.
- A failure of the default project's tasks on Windows and a failure of the WPF app turn the same
  check red; the leg's log names the step.
- `Template acceptance (windows, wpf, local auth)` and `Template acceptance (windows, wpf, external
  auth, notes)` are the Windows checks the `develop` ruleset requires.

## Revisit when

Windows runner time weighs on the pace of pull requests, or a Windows leg needs a step the Windows
job does not run.
