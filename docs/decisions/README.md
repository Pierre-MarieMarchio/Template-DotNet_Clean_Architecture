---
type: reference
title: Decisions
description: When a choice deserves an ADR, how ADRs are numbered and superseded, and which decisions this project holds.
---

# Decisions

An architecture decision record (ADR) says why this project made one choice over another, in a page
of its own: the context, the decision, the alternatives it turned down, its consequences, and when
to revisit it. The code says what the project does; an ADR says why it does not do the obvious
alternative.

## When to write one

Write an ADR for a decision that passes all three tests:

- a competent team could reasonably have decided otherwise;
- a future maintainer must know its consequences before changing it;
- it is not already obvious from the code, the conventions or mainstream .NET practice.

A rule every change follows, or a fact about how something works, belongs on the page that
describes it, not here. Where a test can hold a decision, it does, and the ADR names the test: a
decision and the test that holds it change together, in the same commit.

## Numbering and status

The ADRs [the index](index.md) lists below 1000 shipped with the project and keep their numbers; a
gap in the sequence is a decision about a part this project does not have. This project's own ADRs
are numbered from **1000**, the next free number each time, and start from
[the ADR template](../_page-templates/adr.md) as `Proposed` until the team accepts them. An ADR's frontmatter says `type: decision`, and its `status` is the
state its `Status:` line opens with, so a change of status edits both lines
([adding a page](../README.md#adding-a-page) lists the fields).

A decision is never edited into another. To replace one, write a new ADR and set the old one's
status to `Superseded by` with a link to the new one, and its frontmatter's `status` to
`Superseded`, which the index shows once `docs-index` runs.

An ADR holds every section of the template. "When this decision does not apply" is the one it leaves
out when the decision has no exception: a case it names there is governed by what that section says,
not by the decision.

## Changing an inherited choice

[ADR 0001](0001-adopt-the-generated-baseline.md) lists the choices this project inherited: its
stack, tools, foundations and deployment shape. To reverse or replace one of them, write a product
ADR that names the row, then link that ADR from the row's "Changed by" cell. ADR 0001 stays
Accepted, and its table shows which inherited choices still hold.

## The decisions

[The index](index.md) lists every decision this project holds, by number, with its status and the
question it answers, as each ADR's frontmatter says. Add this project's own ADRs from 1000, then run
`dotnet cleanarch docs-index`, which lists them after the ones the project shipped with.
