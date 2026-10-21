# 0003. Use cases and ports are explicit

Status: Accepted

## Context

The application layer has a way in, from the hosts, and a way out, to storage and the outside world.
Both can be made implicit: a mediator that routes a request object to whichever handler is
registered, and a generic repository or an `IQueryable` that lets each caller compose its own
query. Each saves a few types at first and costs later: a call graph only a runtime registry knows,
a storage contract that is the database itself, and a port that grows until it is the whole
vertical.

## Decision

**The way in.** A controller calls a use case or a service through its interface, and `F12` reaches
the implementation. Every entry point has an interface (`IUseCase<TRequest, TResponse>` for a use
case), for dependency inversion and substitution in tests. There is no MediatR, no dispatcher and no
pipeline behaviour: validation runs in the use case, logging is `ILogger`, the commit is
`IUnitOfWork`.

**The way out.** A port is named for its intent, in one of four words: a `Repository` loads and
saves an aggregate and is declared in the domain beside it; `Queries` projects rows onto a result;
a `Store` serves storage with no aggregate behind it; a `Table` is one table's row access, reached
only by a sibling infrastructure module and never by a use case. No generic repository exists, and
no port exposes `IQueryable`. A port is one capability of at most six operations; a seventh means a
second intent and a split.

## Consequences

- The compiler checks the wiring, and adding a mediator later is mechanical, where removing one that
  every handler assumes is not.
- `LayerDependencyTests` forbids MediatR in the application layer; `StorageVocabularyTests` holds
  the four words; `PortConventionTests` holds the six-operation ceiling and requires every port to
  have a consumer in the application layer; `StorageShapeTests` refuses an `IQueryable` on a port.
- Each new query is a named method on a port, with its own test, rather than an expression a caller
  writes.

[Ports and adapters](../architecture/ports-and-adapters.md) and
[Use cases, not a dispatcher](../architecture/domain-and-results.md#use-cases-not-a-dispatcher)
describe the vocabulary and its rules.

## Revisit when

The project needs behaviour wrapped around every use case that a decorator or the use case itself
cannot carry, or a port's callers keep needing query shapes no named method can anticipate.
