---
type: decision
title: 0041. The solution is an .slnx
description: Why the solution at the root is the XML .slnx format rather than the classic .sln, and what a project holding an .sln does.
status: Accepted
---

# 0041. The solution is an .slnx

Status: Accepted

## Context

The solution at the repository root lists every .NET project, in the solution folder of its layer,
and the tasks, the workflows and the IDEs open it. Two formats describe it. The classic `.sln` is a
line format of its own: each project carries a GUID for its kind and one for itself, and every
platform configuration is repeated per project, so adding a project rewrites dozens of lines and two
branches that each add one conflict. The `.slnx` is XML: a project is one `<Project Path="…" />`
line inside its `<Folder>`, with no GUID and no configuration block. From .NET 10, `dotnet new sln`
writes an `.slnx`, and `dotnet sln`, `dotnet build`, `dotnet test` and `dotnet format` read either.

## Decision

- **The root holds one solution, `AppTemplate.slnx`.** Its folders are the layers', under `Src/`
  and `Tests/`; `presentation-scaffold` and `web-scaffold` add a project with
  `dotnet sln add --solution-folder`, and the tasks, the solution filters they write and the
  workflows name it.
- **No `.sln` beside it or in its place.** `ProjectFileTests` holds that the root's one solution is
  the project's name with the `.slnx` extension.

## Alternatives considered

- **Keeping the `.sln`.** Every tool reads it, the oldest included; the `.slnx` needs the .NET SDK
  9.0.200 or later, MSBuild 17.13, Visual Studio 2022 17.13 or later, or a recent Rider or C# Dev
  Kit. This project pins an SDK 10 in `global.json`, so the command line reads the `.slnx` wherever
  it builds; an IDE older than those versions does not, which is the cost this project accepts for
  diffs a reviewer can read and merges without GUIDs to reconcile.
- **Both files, kept in step.** Every tool opens one of them, and every change to the projects is
  made twice, with nothing but a test to catch the copy that drifts.

## Consequences

- Adding or removing a project changes one line of the solution, and a merge of two branches that
  each add one is a merge of two lines.
- A tool that parses the solution itself, rather than through MSBuild or the SDK, has to read XML.
- A project generated with an `.sln` keeps it until it takes a `Tools/Tasks.cs` that names the
  `.slnx`; the launcher finds the project by its `cleanarch.json`, whatever the solution's format.
  To move, `dotnet sln <Name>.sln migrate` writes `<Name>.slnx` beside it from the projects the
  `.sln` holds, after which the `.sln` is deleted and each place that names it, such as the
  `SOLUTION` of a workflow, moves to the new file; [upgrading](../guides/upgrading.md#what-the-report-compares) says how the report shows it.

## Revisit when

A tool this project depends on reads only the `.sln`, or the SDK's default format changes again.
