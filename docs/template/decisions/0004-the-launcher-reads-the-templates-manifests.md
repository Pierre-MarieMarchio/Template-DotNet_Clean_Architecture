# 0004. The launcher reads the templates' manifests

Status: Accepted

## Context

`dnx CleanArch.Create` asks for each option of `cleanarch-webapi`, `cleanarch-webapp` and
`cleanarch-webpackage`, and explains a refused combination before generating. A launcher that held
its own copy of the options, their choices and the refusals would drift from the templates at the
first option added, and a user would be asked questions the engine no longer accepts.

## Decision

- **The questions come from the templates' files.** Each visible option of `ide.host.json`, in its
  order and under its label, with the choices, defaults and descriptions of `template.json` and the
  long names of `dotnetcli.host.json`. The launcher embeds the three manifests of the commit it is
  packed from; `--template-source` reads them from a checkout or a `.nupkg` instead.
- **A refusal is evaluated, not restated.** The launcher evaluates each refusal's `isRequired`
  condition, and the computed symbols it names, on the answers; it shows the refusal's description
  and asks again the options its condition reads.
- **The launcher and the template package share one version**, the `templateVersion` of
  `.template.config/template.json`, and the launcher installs that version of the package.
- **What the manifests cannot say stays in the launcher and is tested against them**: the web app's
  name and audience are asked only with `--web`, and an item template's project facts (authentication,
  workflows, scope, workspace) are read from the project's files.
- **It lives in `Launcher/`**, its own solution, excluded from generation and from the template
  package.

## Consequences

- An option added to a template reaches the launcher with no change to it, once it is visible in
  `ide.host.json`.
- A refusal must keep its shape, a hidden parameter whose only choice is `refused` with a conditional
  `isRequired`, and `refusedOptions` must stay its union: the launcher's tests fail on every
  combination where the two disagree.
- The launcher's tests read the repository's manifests and generate with the packed templates, so
  `verify` and CI run them beside the template's own gates.

## Revisit when

The template engine exposes a template's parameters and constraints through an API a tool can call,
or an option needs a question the manifests cannot describe.
