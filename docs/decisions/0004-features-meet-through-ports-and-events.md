# 0004. Features meet through ports and events

Status: Accepted

## Context

Features of one service share projects, one database and one container. Nothing stops one from
reading another's tables, using its records or calling its internals, and each such reach is cheap
the day it is written. The cost comes when a feature has to move out, or be removed: every reach
across is an edit the other feature needs before it can let go.

## Decision

A feature uses another only through that feature's public application port or its domain events,
never through its records, its EF sets or its tables. A feature's surface is the `Ports` namespace
of its application folder and the `Events` namespace of its domain folder; everything else of it,
persistence included, is its own.

The shared mechanisms, the `Common/` folders and the HTTP common code know no feature at all. A type
there that names one is a feature adapter filed in the wrong place.

## Consequences

- Each feature stays extractable into a service of its own, and removable, without rework of the
  others.
- `FeatureIsolationTests` reads both the application and the persistence projects, since a query
  can read another feature's rows without any type signature showing it.
  `ModuleDependencyTests` keeps the `Common/` folders free of features.
- A feature that needs another's data asks a port for it. The port belongs to the feature that owns
  the data, which decides what it exposes.
- A fact that several features react to is a domain event, consumed under the rules of
  [ADR 0006](0006-events-after-commit-without-an-outbox.md).
<!--#if (reminders)-->
- Reminders reach TodoLists through `ITodoItemQueries` and react to completion through an event,
  which is this project's example of both channels.
<!--#endif-->

[Features meet through ports and events](../architecture/modules-and-boundaries.md#features-meet-through-ports-and-events)
shows the rule in the folder layout.

## Revisit when

Two features always change together and every port between them mirrors the other's model: they
are one feature, and merging them is cheaper than keeping the boundary.
