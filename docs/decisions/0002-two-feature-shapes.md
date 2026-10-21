# 0002. Two feature shapes

Status: Accepted

## Context

The features of a business API are not alike. Some guard rules over a state: a transition that is
forbidden, a title unique within a list, a cap, two fields that must agree. Others store what the
caller sends and give it back. One shape for both either buries a simple feature under an
aggregate, a mapper and a use case per operation that protect nothing, or leaves a rich feature's
rules scattered over services where nothing holds them together.

## Decision

One question chooses a feature's shape: **is there a state to protect?**

- **No: a service over a store.** No domain model. One service interface with a method per
  operation, a `Store` port for writes and a `Queries` port for reads; the persistence record is the
  model. This is the generator's `--model record`, its default.
- **Yes: an aggregate with use cases.** An aggregate, its value objects and events in the domain;
  one use case per operation; a repository port beside the aggregate; a record, a mapper and a
  tracker in persistence ([ADR 0005](0005-the-domain-is-not-mapped-by-ef.md)). This is
  `--model aggregate`.

Both shapes validate with FluentValidation, commit through `IUnitOfWork` and keep reads free of
writes. Both are first-class. Promotion from simple to rich is the only bridge between them:
extract the aggregate, replace the store by a repository, split the service into use cases.

## Consequences

- A feature's author answers one question before writing code, and the generator writes the shape
  the answer names.
- `FeatureIsolationTests` holds the simple shape to its form: a feature with no domain folder must
  offer a service and its `Store` or `Queries` ports, and may not declare use cases.
- A simple feature that acquires a rule is promoted, not given a check inside its service, so a rule
  always lives in an aggregate that loads everything it guards.
<!--#if (notes)-->
- Notes is this project's example of the simple shape.
<!--#endif-->
<!--#if (todoLists)-->
- TodoLists is this project's example of the rich shape.
<!--#endif-->
<!--#if (files && !todoLists)-->
- Files is this project's example of the rich shape.
<!--#endif-->

The two shapes side by side, and the promotion steps, are in
[Feature shapes](../architecture/feature-shapes.md).

## Revisit when

Several features need a middle shape, rules without an aggregate worth its mapper, or promotion
becomes frequent enough that starting every feature rich costs less.
