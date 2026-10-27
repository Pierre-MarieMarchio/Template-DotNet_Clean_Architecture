# 0009. A .NET project comes from a starter

Status: Accepted

## Context

[0008](0008-a-web-app-comes-from-a-starter.md) made each web app a starter: a pinned CLI that
`web-scaffold` runs with pnpm, and an overlay. It left a `kind` for a starter that is no pnpm app
until one arrived. `--presentation blazor-server` is the first: a Blazor Web App written by
`dotnet new blazor`, from Microsoft's ASP.NET Core templates. Its output is C#, built by the
solution and held by the architecture tests, so it has to come under the repository's rules: the
central package versions, the properties `Directory.Build.props` already sets, the solution, the
formatting. The SDK bundles its own copy of those templates, which is not the version the starter
pins and refuses an install of another version beside it.

## Decision

- **A descriptor says its kind.** `scaffold.json` holds `kind`: `pnpm` when absent, so every web
  starter reads as before, and `dotnet` for a template package. A `dotnet` descriptor names the
  package, its exact version, the template's short name, its arguments, and the generated folders
  the project keeps.
- **The template runs in a hive of its own.** `presentation-scaffold` installs the pinned package
  into a scratch template hive with the SDK's own templates disabled, so the version the descriptor
  names is the one that runs, whatever SDK is installed. The hive sits in the system's temporary
  folder, since the template engine names a lock after its path.
- **The project's own files are the template's.** The starter carries the project file, the
  composition root, the settings, the image and the host's wiring; the generator contributes only
  the folders the descriptor keeps, `Components` and `wwwroot` for Blazor. A project file the
  generator writes is compared with the starter's, and a package or a property the starter's lacks
  stops the scaffold with its name, so a template version that needs more is noticed rather than
  dropped.
- **The project joins the solution once it is created**, with its tests, through `dotnet sln add`,
  then each is formatted. The checkout's solution never names it, since the checkout has no
  generated folders to build it from.
- **A starter is copied by the option whose root it fills.** A web starter holds `FrameworkSite/`
  and is copied into `Src/Web/apps/`; a presentation starter holds its project and its tests at
  their paths and is copied into the project's root. `TemplatePackagingTests` holds that each
  folder under `.template.config/starters` is one or the other, and that each `--presentation`
  value but `none` is a starter of kind `dotnet`.
- **`bootstrap` creates the presentation project first**, before the formatting it then checks.
- **A value names its generator and never changes meaning**, as for `--web`: `blazor-server` is
  `dotnet new blazor` with server interactivity and no authentication of its own.

## Alternatives considered

- **Install into the default hive.** The SDK's bundled templates win, or refuse the pinned version.
- **The generator's project file, edited.** Each edit would parse and rewrite XML whose shape the
  template owns, to add what the starter already knows: its references, its markers, its packages.
- **A solution entry in the template.** The checkout's solution would name a project the checkout
  cannot build.
- **Starters in a folder per option.** A second root to copy from, for what the content of the
  folder already says.

## Consequences

- A .NET framework costs its starter folder: the descriptor, the project's own files and its test
  project, a choice and a computed symbol, a source in `template.json`, and an existing acceptance
  leg it joins.
- The template package's version is bumped by hand with the ASP.NET Core image, as Dependabot reads
  no descriptor.
- `presentation-scaffold` needs the network once, to fetch the template package.

## Revisit when

A starter needs a generated file outside the folders it keeps, or a second .NET generator does not
fit the descriptor.
