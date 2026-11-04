# 0022. A WPF app is one Windows project, tested on Windows

Status: Accepted

## Context

WPF exists to build Windows apps: whoever chooses `--presentation wpf` develops and runs the app on
Windows. The WPF starter wrote two projects, `<Project>.Desktop` on `net10.0-windows` with the XAML
and its code-behind, and `<Project>.Desktop.Core` on `net10.0` with the view models, settings, host
and session, so that the view-model tests ran on the template's Linux runners and in WSL. That
split served the template's constraint, not the user's: the Avalonia starter writes one project, and
[0014](0014-a-windows-only-shape-is-proved-on-a-windows-runner.md) already proves a Windows-only
shape on a Windows runner. The split also cost every tool around it a case of its own:
`presentation-scaffold` added a sibling, `cleanarch-presentation` refused names whose `.Core` was
taken, and three architecture rules carried an exception for it. A test project on a Windows target
framework builds on Linux but its tests cannot run there, so the tasks that run the tests must know
it.

## Decision

- **One project.** The WPF starter writes `<Project>.Desktop` alone, on `net10.0-windows` with
  `UseWPF` and `EnableWindowsTargeting`, laid out as the Avalonia starter's: the XAML and its
  code-behind, `ViewModels/`, `Common/Hosting` and `Common/Sessions`, referencing the .NET client
  alone. Its one test project, `<Project>.Desktop.UnitTests`, is on `net10.0-windows` too. The two
  Windows acceptance legs run it.
- **The tasks skip it off Windows, and say so.** Off Windows, `test`, `check`, `coverage` and
  `verify` leave out each test project whose own project file sets a Windows `<TargetFramework>`,
  through the solution filters they already write, and print
  `Skipped <folder>: it targets <framework> and runs on Windows alone.` before the run and after it;
  the build still compiles it and the exit code is the others'. `sonar` does the same, its scanner
  running on Linux. The `sqlite, defaults` leg fails unless `test --no-integration` announces the
  skip. `Tools/Tasks.cs --self-test` holds the matcher and the selection, and
  `WindowsTestProjectTests` holds the premise: such a test project says so in its own file, with one
  target framework, and no `.props` or `.targets` sets a Windows one.
- **The session stays a copy.** The WPF starter's session, its sign-in settings and their tests are
  the Avalonia starter's, byte for byte, at the same paths;
  `TheWpfStartersDesktopSession_IsTheAvaloniaStartersOwn_ByteForByte` holds it.

## Alternatives considered

- **Keep the `.Core` split, so the view-model tests run on Linux.** It shapes every WPF project
  around the template's runners, which its users do not have, and keeps the special cases in the
  scaffold, the item template and the architecture rules.
- **Read the target framework with `dotnet msbuild -getProperty:TargetFramework`.** It sees a value
  set by a `.props` file too, but costs an evaluation per test project on every run, `check` among
  them, which is meant to take under a minute; reading the file, held by a rule, costs nothing.
- **Set `IsTestProject` to `false` off Windows.** `dotnet test` would leave the project out by
  itself, but in silence: a reader of a green run on Linux could not tell that the app's tests never
  ran.
- **One session source shared by both desktop starters.** A shared project or linked files would
  add a project the Avalonia app does not need, or a file outside each app's folder that
  `cleanarch-presentation` would have to bring; a third desktop client is the time for it, and until
  then a byte-identity test keeps the two copies equal.

## Consequences

- A Linux or WSL run of a WPF project's tests proves everything but the app; its tests run on
  Windows, on the developer's machine and on the Windows acceptance legs.
- A test project of any kind that targets Windows is skipped off Windows the same way, as long as
  it names its target framework in its own file.
- A project generated with `--presentation wpf` before this decision keeps its `.Core` project; to
  take this shape, it moves the `.Core` project's folders into the app, its tests into
  `<Project>.Desktop.UnitTests`, and drops the `.Core` projects from the solution.
- ADR 0001's row for the WPF app describes one project developed, run and tested on Windows.

## Revisit when

A WPF project needs tests that run where Windows does not, or a third desktop client asks for a
session shared by every starter.
