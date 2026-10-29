# The template

These pages are about the `dotnet new` template this repository is, not about the project it
generates. They never reach a generated project; every other page under `docs/` does, conditioned on
the options chosen.

## Using the template

[Using the template](using-the-template.md): generate a project with the launcher or `dotnet new`,
choose the options, and the combinations it refuses.

## Maintaining it

[Maintaining the template](maintaining-the-template.md): the template's own gates, the acceptance
matrix, adding an option, and writing pages that condition on the options.

## What is coming

[Roadmap](roadmap.md): the items coming in order, the ones for later, what stays out, and how to
propose one.

## Its decisions

- [0001. Options remove parts and refuse combinations](decisions/0001-options-remove-parts-and-refuse-combinations.md)
- [0002. Nothing about the template reaches a project](decisions/0002-nothing-about-the-template-reaches-a-project.md)
- [0003. Framework apps come from official CLIs](decisions/0003-framework-apps-come-from-official-clis.md), superseded by 0008
- [0004. The launcher reads the templates' manifests](decisions/0004-the-launcher-reads-the-templates-manifests.md)
- [0005. A project carries one database provider](decisions/0005-a-project-carries-one-database-provider.md)
- [0006. develop integrates, main holds the releases](decisions/0006-develop-integrates-main-holds-the-releases.md)
- [0007. GitHub Packages mirrors nuget.org](decisions/0007-github-packages-mirrors-nuget-org.md)
- [0008. A web app comes from a starter](decisions/0008-a-web-app-comes-from-a-starter.md)
- [0009. A .NET project comes from a starter](decisions/0009-a-dotnet-project-comes-from-a-starter.md)
- [0010. A pull request runs the legs its change can affect](decisions/0010-a-pull-request-runs-the-legs-its-change-can-affect.md)
- [0011. A starter may render on the server](decisions/0011-a-starter-may-render-on-the-server.md)
