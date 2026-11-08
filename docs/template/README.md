---
type: reference
title: The template
description: Which page covers using or maintaining the dotnet new template this repository is.
---

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

[The index of the template's decisions](decisions/index.md) lists each one by number, with its
status and the question it answers; [the index](index.md) lists these pages. A template decision
follows the project's rule: a last resort, for a choice the pages about the template cannot carry,
as [when to write one](../decisions/README.md#when-to-write-one) says.
