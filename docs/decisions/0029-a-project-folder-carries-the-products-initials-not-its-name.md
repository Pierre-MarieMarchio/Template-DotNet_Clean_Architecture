# 0029. A project folder carries the product's initials, not its name

Status: Accepted

## Context

A .NET project's folder is usually its name, so every path under `Src` and `Tests` repeats the
product's name, and a deep feature file repeats it twice: in the project's folder and, for a test,
in its test project's folder. A longer product name lengthens every path by as much. Windows limits a
path to 260 characters unless git's `core.longpaths` and the system's long-path setting are both
turned on, and NuGet warns (NU5123) for a packed file whose path passes 200; a product with a long
name, checked out a few folders deep, reaches either limit.

## Decision

- **A project's folder is its name with the product's name replaced by the product's initials**:
  the capital letters of the name in order, or a part's first letter where the part has none
  (`Acme.Shop` gives `AS`, `Contoso.OrderManagement` gives `COM`). `<Name>.Application.Auth` sits in
  `Src/Application/<Initials>.Application.Auth`, and its tests in
  `Tests/Application/<Initials>.Application.Auth.UnitTests`.
- **The project's name is unchanged**: the `.csproj` file, the assembly, the root namespace and the
  solution's entries keep the whole name.
- **The layer folders stay**, so the tree still reads by layer.
- `ProjectFolderTests` holds every project to its folder; the tasks and the generators compute a
  folder from the name `cleanarch.json` records.

## Alternatives considered

- **Leaving the folders as the projects' names**: the paths grow with the name, and a long name in a
  deep checkout fails on Windows.
- **Dropping the layer folders**: shorter still, but the tree would not read by layer, and the rules
  that find a project by its layer change with it.
- **Shortening the feature folders instead**: they name what the code does, and the product's name
  says nothing a reader of the path needs.

## Consequences

- A folder does not match its project's name: a path is found from the name by replacing the
  product's name with its initials, and the IDE's solution explorer shows the projects by name,
  grouped by layer.
- A module promoted to a project of its own follows the rule, `<Name>.Domain.Billing` in
  `Src/Domain/<Initials>.Domain.Billing`; a second product with the same initials goes in a root
  folder of its own, as the tasks find a project by its `cleanarch.json`.
- Moving a project means updating the paths that cite it: the solution, the references between
  projects, the `Dockerfile`s, the workflows and the pages.

## Revisit when

Windows and NuGet lift their path limits by default, or a product's initials collide with another
part of its tree.
