---
type: decision
title: 0026. The launcher and the project's tool read the templates' manifests
description: Why the launcher and the project's tool both ask their questions from the templates' own manifests.
status: Accepted
---

# 0026. The launcher and the project's tool read the templates' manifests

Status: Accepted; supersedes [0004](0004-the-launcher-reads-the-templates-manifests.md)

## Context

`dnx CleanArch.Create` asks for the options of `cleanarch-webapi`, and of the item templates
`cleanarch-webapp`, `cleanarch-webpackage` and `cleanarch-presentation` when a part is added, and
explains a refused combination before anything is generated; the project's own tool asks the item
templates' questions once it carries the additions
([0019](0019-the-launcher-creates-and-the-project-tool-adds.md)). A tool that held its own copy of
the options, their choices and the refusals would drift from the templates at the first option
added, and would ask questions the engine no longer accepts. Some of what a tool asks the manifests
cannot say: the facts of the project an item template runs in, and a refusal that reads an item's
name.

## Decision

- **The questions come from the templates' files.** Each visible option of `ide.host.json`, in its
  order and under its label, with the choices, defaults and descriptions of `template.json` and the
  long names of `dotnetcli.host.json`. A tool embeds the manifests of the commit it is packed from;
  the launcher's `--template-source` reads them from a checkout or a `.nupkg` instead.
- **A refusal is evaluated, not restated.** The tool evaluates each refusal's `isRequired` condition,
  and the computed symbols it names, on the answers; it shows the refusal's description and asks
  again the options its condition reads.
- **A question offers only what some answer to the rest can generate.** Before each question the
  tool evaluates the refusals over every value of the options not answered yet, and leaves out a
  choice no such value saves, such as `--examples all` after `--auth none`. A question left with one
  choice is answered with it and the answer that leaves it, as "the only option when Authentication
  is none", or with the refusal's description when no answer does. Without questions it is taken
  the same way: with `--yes` for any option, rather than its default, and without `--yes` for a
  choice that has no default.
- **A multi-valued option is one multi-select.** An option declared with `allowMultipleValues`,
  `--presentation`, is asked as a multi-select that keeps at least one value, passed to `dotnet new`
  as the option repeated once per value; its conditions read `==` as "among the values" and `!=` as
  "not among them", as the engine does. A combination its refusals reject is explained and the
  selection asked again. When what was chosen calls the API, or holds neither the API nor the
  worker, the tool shows the API or the worker as answered for you, naming why, and passes it
  among the values ([0020](0020-a-project-is-the-presentation-projects-it-holds.md)).
- **A tool and the template package share one version**, the `templateVersion` of
  `.template.config/template.json`, and the launcher installs that version of the package.
- **What the manifests cannot say stays in the tool and is tested against them**: the web app's
  name, audience and rendering are asked only with `--web`; an item template's project facts come
  from the project, not from a question; and a refusal that reads an item's `-n` is checked on the
  name asked.
- **They live in `launcher/`**, one solution with the code they share in `launcher/CleanArch.Tooling`,
  excluded from generation and from the template package.

## Alternatives considered

- **The options written into the launcher.** No manifest to parse, and a launcher that drifts from
  the templates at the first option added.
- **The refusals restated as the launcher's own rules.** A refusal changed in `template.json` and not
  in the launcher would let a user answer into a combination the engine refuses.
- **Every choice offered, refused after the answers.** A user would answer a question, then learn
  that an earlier answer ruled it out.

## Consequences

- An option added to a template reaches the tools with no change to them, once it is visible in
  `ide.host.json`.
- A refusal must keep its shape, a hidden parameter whose only choice is `refused` with a
  conditional `isRequired`, and `refusedOptions` must stay its union: the launcher's tests fail on
  every combination where the two disagree.
- The launcher's tests read the repository's manifests and generate with the packed templates, so
  `verify` and CI run them beside the template's own gates.

## Revisit when

The template engine exposes a template's parameters and constraints through an API a tool can call,
or an option needs a question the manifests cannot describe.
