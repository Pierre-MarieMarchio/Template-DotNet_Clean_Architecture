---
type: decision
title: 0034. Features meet through ports their owner declares, and events
description: Why a feature reaches another only through a port that other declares or through its domain events.
status: Accepted
---

# 0034. Features meet through ports their owner declares, and events

Status: Accepted; supersedes ADR 0004

## Context

Features of one service share projects, one database and one container. Nothing stops one from
reading another's tables, using its records or calling its internals, and each such reach is cheap
the day it is written. The cost comes when a feature has to move out, or be removed: every reach
across is an edit the other feature needs before it can let go.

A feature needs another in three ways: to read its state before deciding, to change its state as
part of its own operation, and to react to what it did. Some requests are answered by several
features at once, as when every feature that holds an owner's data is asked for it. A rule that
says only "through a port" leaves open who declares that port: the feature that calls it, the
feature that owns the data, or a shared folder.

## Decision

- **A feature reaches another through a port or a domain event, and nothing else**: never its
  records, its EF sets, its tables, its aggregates, its services or its use cases. A feature's
  surface is the `Ports` namespace of its application folder and the `Events` namespace of its domain
  folder; everything else of it, persistence included, is its own.
- **The feature that owns the data declares the port**, under `Ports/<Port>/` in its own application
  folder, and decides what it exposes. A read port is implemented by the persistence module, under
  that feature's `Access/Queries/`. A write port is implemented by the owner's own service, which
  loads the owner's aggregate and calls the method that holds the rule, so the owner's invariants
  guard its state; it stages and does not commit, and answers a status that closes the ways the call
  can go, never a `Result`, so that the caller chooses its error code. The calling use case commits
  through `IUnitOfWork`, and both features' rows, in the business context, commit together or not
  at all.
- **A request several features answer is declared once in the application core**, under
  `Common/Ports/` of `AppTemplate.Application.Core`, which names no feature; each owner implements
  it, and the caller takes every implementation.
- **A fact others react to is a domain event**, raised by the feature that made it true and consumed
  after the commit, under the rules of [ADR 0006](0006-events-after-commit-without-an-outbox.md). If
  nobody needs an answer, it is an event; if the operation needs one, it is a port.
- **The shared code knows no feature.** The `Common/` folders and the HTTP common code name no
  feature; a type there that names one is a feature adapter filed in the wrong place.
- `FeatureIsolationTests` holds the reach, reading both the application and the persistence
  projects, since a query can read another feature's rows without any type signature showing it.
  `ModuleDependencyTests` keeps the `Common/` folders free of features, `PortConventionTests`
  refuses a port nobody calls, and `ContainerCompositionTests` a port with no registration.

## Alternatives considered

- **Reaching another feature's records, sets or tables.** No port to write, and an edit in the
  other feature before either can move or go.
- **The calling feature declares the port it needs.** The owner would then implement an interface
  from each caller's folder, depending on the features that depend on it, and removing a caller
  would edit the owner.
- **Sending the other feature's use case from a handler.** A use case inside a use case: two
  commits, the pipeline run twice, and a caller bound to a message shaped for a host.
- **A change of another feature's state as an event.** It commits apart from the operation and at
  most once, so a state that must change with the operation can be left behind.
- **A port per pair of features in a shared folder.** The shared code would name features, and the
  folder would grow with every pair.

## Consequences

- Each feature stays extractable into a service of its own, and removable, without rework of the
  others.
- A host that composes a feature composes every feature whose ports it calls: `Add<A>` needs
  `Add<B>` in the same host.
- A write port ties two features to one commit in one context. A feature moved out of the business
  context turns its write ports into remote calls, which no longer commit with the caller.
<!--#if (reminders)-->
- Reminders reaches TodoLists through `ITodoItemQueries` and reacts to completion through an event,
  which is this project's example of a read port and of an event.
<!--#endif-->
<!--#if (anyFeature)-->
- `IDeletedAccountDataPolicy` is this project's example of a port declared in the core: every
  feature that owns user data implements it, and the account-deletion sweep calls each one.
<!--#endif-->

[Features meet through ports and events](../architecture/modules-and-boundaries.md#features-meet-through-ports-and-events)
shows the rule in the folder layout, and [reaching another feature](../guides/reaching-another-feature.md)
gives the steps of each way in.

## Revisit when

Two features always change together and every port between them mirrors the other's model, which
makes them one feature; or a feature must leave the business context, which ends the shared commit a
write port relies on.
