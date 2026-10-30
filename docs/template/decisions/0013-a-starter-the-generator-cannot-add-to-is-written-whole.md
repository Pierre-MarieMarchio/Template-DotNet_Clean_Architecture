# 0013. A starter the generator cannot add to is written whole

Status: Accepted

## Context

[0009](0009-a-dotnet-project-comes-from-a-starter.md) makes a .NET project a starter: a descriptor
names a template package at an exact version, `presentation-scaffold` runs it in a hive of its own
and keeps the folders the descriptor names, and the starter owns the rest. For the worker of
`--presentation worker`, the generator writes nothing the project would keep, and the WPF app of
`--presentation wpf` has no generator a descriptor could pin:

| Host | Generator, as checked | What it writes | What the project would keep |
|---|---|---|---|
| `--presentation worker` | `dotnet new worker`, from `Microsoft.DotNet.Web.ProjectTemplates.10.0` 10.0.14 | a `Microsoft.NET.Sdk.Worker` project on `Microsoft.Extensions.Hosting`, `Program.cs` on `Host.CreateApplicationBuilder`, a sample worker class, the settings and the launch settings | nothing: the starter owns the project, `Program.cs`, the settings and the launch settings, and the sample goes |
| `--presentation wpf` | `dotnet new wpf`, from `Microsoft.DotNet.Wpf.ProjectTemplates` 10.0.9-servicing.26270.113, which ships with the .NET SDK alone: nuget.org stops at 3.0.0, so no version can be installed into a hive | a `WinExe` on `net10.0-windows` with `UseWPF`, `App.xaml` with a `StartupUri`, an empty `MainWindow.xaml`, their code-behind and an assembly information file declaring `ThemeInfo` | nothing: the starter owns the project, the window and the code-behind, `App.xaml` names no start-up window since `App.xaml.cs` starts the host first, and the assembly information file goes, the app having no custom control theme |

## Decision

- **Such a starter holds the whole project.** Its descriptor says `"kind": "written"` and names the
  generator, the package and the version whose shape it follows; it names no template to run and
  keeps no folder. `presentation-scaffold` adds the project and its tests to the solution, formats
  them and deletes the descriptor, as for any starter, without the network.
- **It follows its generator's shape.** The SDK, the entry point and the settings files are the ones
  the generator writes at the version the descriptor names, under the repository's rules: central
  package versions, the properties `Directory.Build.props` already sets, the formatting.
- `TemplatePackagingTests` holds that a descriptor is of kind `dotnet`, with the template it runs,
  or `written`, with the generator it follows and a project file of its own.

## Alternatives considered

- **Run the generator and keep nothing.** The network and a pinned package, to throw the output away.
- **Run the generator and keep its sample.** A class the project deletes on its first change.
- **Run the SDK's own `dotnet new wpf`, unpinned.** Each machine would write the shape of the SDK
  it has, and a template the SDK no longer carried would break `bootstrap`.

## Consequences

- Creating the worker or the WPF app needs no network and gives the same project on every machine.
- A change in the generator's output is noticed only when someone reads it: bumping the version a
  descriptor names means generating with it and comparing.
- A written starter's C# is compiled only in a generated project, as any starter's is.

## Revisit when

The generator comes to write something the project would keep, or a WPF template package is
published at the SDK's versions, so that a descriptor could pin it.
