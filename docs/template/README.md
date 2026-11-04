# The template

These pages are about the `dotnet new` template this repository is, not about the project it
generates. They never reach a generated project; every other page under `docs/` does, conditioned on
the options chosen.

## Using the template

[Using the template](using-the-template.md): generate a project with the launcher or `dotnet new`,
choose the options, the combinations it refuses, and how a project reads what a newer version
changes.

## Maintaining it

[Maintaining the template](maintaining-the-template.md): the template's own gates, the acceptance
matrix, adding an option, writing pages that condition on the options, and what the upgrade report
needs kept true at each release.

## What is coming

[Roadmap](roadmap.md): the items coming in order, the ones for later, what stays out, and how to
propose one.

## Its decisions

- [0001. Options remove parts and refuse combinations](decisions/0001-options-remove-parts-and-refuse-combinations.md)
- [0002. Nothing about the template reaches a project](decisions/0002-nothing-about-the-template-reaches-a-project.md), superseded by 0018
- [0003. Framework apps come from official CLIs](decisions/0003-framework-apps-come-from-official-clis.md), superseded by 0008
- [0004. The launcher reads the templates' manifests](decisions/0004-the-launcher-reads-the-templates-manifests.md), superseded by 0026
- [0005. A project carries one database provider](decisions/0005-a-project-carries-one-database-provider.md)
- [0006. develop integrates, main holds the releases](decisions/0006-develop-integrates-main-holds-the-releases.md), superseded by 0027
- [0007. GitHub Packages mirrors nuget.org](decisions/0007-github-packages-mirrors-nuget-org.md)
- [0008. A web app comes from a starter](decisions/0008-a-web-app-comes-from-a-starter.md), superseded by 0023
- [0009. A .NET project comes from a starter](decisions/0009-a-dotnet-project-comes-from-a-starter.md)
- [0010. A pull request runs the legs its change can affect](decisions/0010-a-pull-request-runs-the-legs-its-change-can-affect.md)
- [0011. A starter may render on the server](decisions/0011-a-starter-may-render-on-the-server.md), superseded by 0023
- [0012. Where the jobs run is an option](decisions/0012-where-the-jobs-run-is-an-option.md), superseded by 0024
- [0013. A starter the generator cannot add to is written whole](decisions/0013-a-starter-the-generator-cannot-add-to-is-written-whole.md)
- [0014. A Windows-only shape is proved on a Windows runner](decisions/0014-a-windows-only-shape-is-proved-on-a-windows-runner.md)
- [0015. An MCP server is a presentation project, written whole](decisions/0015-an-mcp-server-is-a-presentation-project-written-whole.md), superseded by 0025
- [0016. The launcher draws only glyphs every console font carries](decisions/0016-the-launcher-draws-only-glyphs-every-console-font-carries.md)
- [0017. A project runs its tasks from a pinned .NET tool](decisions/0017-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md)
- [0018. A project keeps its provenance and its tool from the template](decisions/0018-a-project-keeps-its-provenance-and-its-tool-from-the-template.md), supersedes 0002
- [0019. The launcher creates, and the project's tool adds](decisions/0019-the-launcher-creates-and-the-project-tool-adds.md)
- [0020. A project is the presentation projects it holds](decisions/0020-a-project-is-the-presentation-projects-it-holds.md)
- [0021. A project reads a newer version by generating both](decisions/0021-a-project-reads-a-newer-version-by-generating-both.md)
- [0022. A WPF app is one Windows project, tested on Windows](decisions/0022-a-wpf-app-is-one-windows-project-tested-on-windows.md)
- [0023. A web app comes from a starter, static or server-rendered](decisions/0023-a-web-app-comes-from-a-starter-static-or-server-rendered.md), supersedes 0008 and 0011
- [0024. The worker takes the jobs out of the API](decisions/0024-the-worker-takes-the-jobs-out-of-the-api.md), supersedes 0012
- [0025. An MCP server is a presentation project, written whole](decisions/0025-an-mcp-server-is-a-presentation-project-written-whole.md), supersedes 0015
- [0026. The launcher and the project's tool read the templates' manifests](decisions/0026-the-launcher-and-the-project-tool-read-the-templates-manifests.md), supersedes 0004
- [0027. develop integrates, main holds the releases](decisions/0027-develop-integrates-main-holds-the-releases.md), supersedes 0006
