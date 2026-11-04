---
type: reference
title: Documentation
description: Which page answers a question about this project, and where a new page goes.
---

# Documentation

Which page answers your question, and where a new page goes.

## How these pages are organised

| Folder | A page belongs here when the reader is |
|---|---|
| [getting-started](getting-started/index.md) | new to this project, reading once and in order, from [the overview](getting-started/overview.md) |
| [architecture](architecture/index.md) | learning the rules every layer, feature and endpoint follows |
| [features](features/index.md) | using, configuring or removing one capability |
| [guides](guides/index.md) | doing a task again: add, remove, migrate, test, ship |
| [reference](reference/index.md) | looking up a setting, a route, a code or a folder |
| [decisions](decisions/index.md) | asking why a choice was made |

[The index](index.md) links each folder's own index, which lists its pages by their title and
their description. `dotnet run Tools/Tasks.cs docs-index` writes every index from the frontmatter of
the pages this project holds, so a page appears there once it exists, and leaves once it is gone.

## Decisions

[The decisions](decisions/README.md) say when a choice deserves an ADR, and
[their index](decisions/index.md) lists every one with its status.
[ADR 0001](decisions/0001-adopt-the-generated-baseline.md) records the choices this project started
with.

## Adding a page

| You add | Where | Start from |
|---|---|---|
| A feature | `features/<feature>.md` | [the feature template](_page-templates/feature.md); `new-feature` writes it for you |
| A how-to | `guides/<task>.md` | [the how-to template](_page-templates/how-to.md) |
| A decision | `decisions/NNNN-<title>.md`, numbered from 1000 | [the ADR template](_page-templates/adr.md) |
| A setting | a section in the configuration reference | the table shape there |
| A route or a code | a row in the API reference | the table shape there |

Then run `dotnet run Tools/Tasks.cs docs-index`, which lists the new page in its folder's index;
`new-feature` and `remove-feature --apply` run it for the page they write or delete. `hygiene` fails
while an index no longer lists its folder's pages as their frontmatter says.

Every page opens with its frontmatter, at the first byte of the file, before its `# ` heading:

```yaml
---
type: guide
title: Adding a feature
description: How to add a feature of your own, in the shape its rules call for, with its tests and its page.
---
```

| Field | Holds |
|---|---|
| `type` | the folder's: `guide` under getting-started and guides, `concept` under architecture, `reference` under reference, `feature` under features, `decision` under decisions; a folder's `README.md` takes the one that fits it |
| `title` | the page's `# ` heading, word for word; the index lists the page under it |
| `description` | one sentence: the question the page answers, which the index shows beside its title |
| `status` | on a decision only: the state its `Status:` line opens with, such as `Proposed`, `Accepted` or `Superseded` |
| `tags` | optional, a list such as `[persistence, http]`; left out rather than empty |

A template carries `type: template` and a placeholder in each other field: set the type of the page's
folder and replace every placeholder. `DocumentationFrontmatterTests` fails on a page whose
frontmatter is missing, holds a type outside the list, or disagrees with its heading or its status.

<!--#if (agentFiles)-->
[AGENTS.md › Writing docs](../AGENTS.md#writing-docs) holds the rules for writing them.
<!--#endif-->
