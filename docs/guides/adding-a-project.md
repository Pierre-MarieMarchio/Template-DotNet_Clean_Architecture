---
type: guide
title: Adding a project
description: How to add a project to the solution so that it builds, ships in each host's image, has its tests and is read by every structural rule.
---

# Adding a project

## When

A capability needs a project of its own: an infrastructure module over a technology the existing
modules do not hold, most often. At the end, the project builds in the solution and in each host's
image, has its unit test project, and every structural rule reads it.

A module holds all the technology of one capability, and the seam that buys portability is the port
it implements, one layer in, as [modules and boundaries](../architecture/modules-and-boundaries.md#infrastructure-per-capability)
says. A new folder in an existing project is not a new project.

## Steps

1. Name it `AppTemplate.<Layer>.<Capability>` and put it under `Src/<Layer>/`, in a folder that
   carries the product's initials in place of its name, `<Initials>.<Layer>.<Capability>`, as the
   folders beside it do ([ADR 0029](../decisions/0029-a-project-folder-carries-the-products-initials-not-its-name.md));
   `ProjectFolderTests` names the folder a misplaced project belongs in.
2. Write its project file from a sibling's: packages without a version, which
   `Directory.Packages.props` pins, and only the references its layer allows. An infrastructure
   module references the application projects whose ports it implements and
   `AppTemplate.Infrastructure.Core`, never another module; `ModuleDependencyTests` holds it. It
   grants its internals to its unit tests with an `InternalsVisibleTo` item.
3. Add it to the solution: `dotnet sln AppTemplate.sln add <path to the project file>`.
4. Give it its registration class at its root, `<Capability>Module` with an
   `Add<Capability>Module` extension method, the only source file a project root holds. Its adapters
   are internal, so a host composes them through that method alone; `AdapterVisibilityTests` holds it.
5. Reference it from each host that composes it, and call `Add<Capability>Module` in that host's
   `Program.cs`. Only a host references an infrastructure module.
6. Add its project file's `COPY` line to the `Dockerfile` of each of those hosts, beside the other
   project files, before the restore. The build passes on your machine without it; only the image
   fails, in the CI's image job.
7. Write its unit test project, `Tests/<Layer>/<Initials>.<Layer>.<Capability>.UnitTests/`, from a
   sibling's, and add it to the solution; `TestProjectMirrorTests` names a project without one.
8. Reference the project from `Tests/Architecture/AT.Architecture.Tests`, as it references every
   module: the rules load each project of a layer by name, and refuse to run over one they cannot
   load.
9. Give it its two entries in `LayoutConventionTests`: its words under `Features/` in `_vocabulary`,
   `null` when it has no `Features/` folder and an empty list when its files sit side by side under
   each feature, and its words under `Common/` in `_commonVocabulary`.
   `EveryProjectOnDisk_HasAVocabularyOfItsOwn` fails until both exist, and the folder rules check
   each word in both directions.
10. For an infrastructure module, add it by hand to the two lists of `ModuleDependencyTests` that
    name modules: `_moduleCommons`, so that `EveryModuleCommon_KnowsNoFeature` reads its `Common/`,
    and `_modulesThatMustNotReachIntoPersistence`. Nothing fails while it is missing from them, so
    this step is yours to check.
11. Compose it in `HostComposition` of the architecture tests, in each composition whose host calls
    it, so that `ContainerCompositionTests` resolves its ports under strict validation.
12. Write its words into the folder map of [the project layout](../reference/project-layout.md#the-folder-map),
    its row into [layers and projects](../architecture/layers-and-projects.md) and, for a module, into
    the modules table of [modules and boundaries](../architecture/modules-and-boundaries.md#the-modules).
    A setting it reads follows [adding a setting](adding-a-setting.md).

A `.Core` project, an SDK half the layer shares, also sets `IsSdkProject`, as
[adding a project](../reference/project-layout.md#adding-a-project) in the project layout says.

## Check

`dotnet run Tools/Tasks.cs test --no-integration` passes, and `docker build` of each host that
references the project, as [deployment](deployment.md#the-image) shows, restores it.

## Undo

Remove the project and its unit test project from the solution and the disk, then each reference,
call, `COPY` line, list entry and row the steps added.
