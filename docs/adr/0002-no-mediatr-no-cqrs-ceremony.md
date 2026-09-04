# 0002 — No MediatR, no CQRS ceremony

Status: Accepted

## Context

Most Clean Architecture templates route every operation through MediatR: a `Command` or
`Query` record, an `IRequest<TResponse>` marker, a matching `IRequestHandler<,>`, and a
runtime dispatch through `ISender`. The payoff is pipeline behaviours — one place to add
validation, logging, transactions and caching for every operation at once.

This template inherited something worse than either option: a hierarchy of
`BaseCreateUseCase` / `BaseUpdateUseCase` / `BaseGetAllUseCase` generic base classes,
plus a per-feature `Manager` layer that mostly forwarded calls, plus interfaces for each
of those. Reaching the code that did the work meant three hops through types that
existed only to be inherited from.

## Decision

A use case is **a plain sealed class with a constructor and one `ExecuteAsync` method**,
registered explicitly in DI and injected into a controller. No mediator, no marker
interfaces, no base classes, no `Manager` layer.

The parts of CQRS that pay for themselves are kept, as **two ports rather than two
stacks**: `ITodoListRepository` for writes (loads and stages whole aggregates) and
`ITodoListQueries` for reads (projects to DTOs in SQL, no aggregate materialisation, no
change tracking).

## Consequences

- `F12` on a use case goes to the code, not to a marker interface. The call graph is a
  call graph, and a stack trace names the method that failed.
- Wiring is checked by the compiler. A missing registration is a startup failure with a
  type name, not a runtime `InvalidOperationException` from a registry lookup.
- Each cross-cutting concern is solved individually and visibly: FluentValidation runs
  inside the use case, logging is `ILogger`, and the transaction boundary is
  `IUnitOfWork`. There is no single pipeline to add a fifth concern to, so the fifth
  concern has to be written in each use case that needs it — the honest cost of this
  decision.
- Controllers take one constructor parameter per use case, so `TodoListsController` has
  eight. That is visible coupling; a mediator would have hidden it behind one `ISender`
  without reducing it.
- Reads do not load aggregates, so a list view costs one projection instead of a full
  aggregate materialisation per row.

## Alternatives rejected

- **MediatR.** Adds three types and a runtime dispatch to reach one method. Worth it
  once there is a real pipeline — retries, idempotency keys, an outbox, transaction
  scoping shared by fifty handlers. A template has none of those yet, and adding
  MediatR later is mechanical: the use case becomes a handler. Removing it once every
  handler assumes it exists is not. (There is also a licensing consideration for
  MediatR v13+, but the argument above holds regardless.)
- **Generic use-case base classes** (what was there). The base class can only contain
  what is common to all entities, which is CRUD; every use case with a real rule then
  overrides it and the base contributes nothing but indirection.
- **A `Manager` layer between controller and use case.** It forwarded calls. Two names
  for one operation, with no rule about which one holds the logic.
- **Full CQRS with separate read and write models/stores.** The right answer when read
  and write load diverge enough to scale separately. For one PostgreSQL database it is
  eventual consistency and a projection pipeline bought with no need.

## Revisit when

Three or more concerns need to apply to *every* operation uniformly — at which point a
pipeline is cheaper than repeating them, and MediatR (or a hand-rolled decorator) earns
its place.
