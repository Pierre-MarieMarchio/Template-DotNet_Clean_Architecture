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

## Its decisions

- [0001. Options remove parts and refuse combinations](decisions/0001-options-remove-parts-and-refuse-combinations.md)
- [0002. Nothing about the template reaches a project](decisions/0002-nothing-about-the-template-reaches-a-project.md)
- [0003. Framework apps come from official CLIs](decisions/0003-framework-apps-come-from-official-clis.md)
- [0004. The launcher reads the templates' manifests](decisions/0004-the-launcher-reads-the-templates-manifests.md)
