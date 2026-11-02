# 0003. Use cases and ports are explicit

Status: Accepted

## Context

The application layer has a way in, from the hosts, and a way out, to storage and the outside world.
Both can be made implicit: a mediator that routes a request object, through a registry only the
running process knows, to whichever handler was registered last, and a generic repository or an
`IQueryable` that lets each caller compose its own query. Each saves a few types at first and costs
later: a call graph nobody can read before run time, a storage contract that is the database itself,
and a port that grows until it is the whole vertical.

The way in also carries what every use case needs and none should write: refusing invalid input
before anything is read or written, a trace and a duration per operation, a log line when one fails.
Written by hand in each use case, nothing guarantees it is there.

## Decision

**The way in.** A use case is a message and its handler: a `<UseCase>Command` or `<UseCase>Query`
record and the one class that handles it, side by side in the use case's own file, with its
FluentValidation validator beside them. A host sends the message through `ISender`. The mediator is
[Mediator](https://github.com/martinothamar/Mediator) (MIT): its source generator runs in each host
and writes the dispatch at compile time, one typed `Send` per message, and refuses to build when a
message has no handler or two. MediatR is refused: its licence is commercial.

Three pipeline behaviours, registered once and in this order by `AddUseCasePipeline`, which every
feature's registration calls, wrap every message: tracing and a duration histogram, a log line for a
failed use case, then the message's validators. A message its validator refuses never reaches its
handler; the behaviour answers a failed `Result`, which `Result` and `Result<T>` can build without the
behaviour knowing `T`, through `IFailable<TSelf>`. The commit stays `IUnitOfWork`, called by the
handler. Domain events keep their own after-commit delivery
([ADR 0006](0006-events-after-commit-without-an-outbox.md)).

**The way out.** A port is named for its intent, in one of four words: a `Repository` loads and
saves an aggregate and is declared in the domain beside it; `Queries` projects rows onto a result;
a `Store` serves storage with no aggregate behind it; a `Table` is one table's row access, reached
only by a sibling infrastructure module and never by a use case. No generic repository exists, and
no port exposes `IQueryable`. A port is one capability of at most six operations; a seventh means a
second intent and a split.

## Alternatives considered

- **A use case called through an interface of its own.** It reads well in a controller, but every
  cross-cutting step is copied into each use case, and nothing fails when one is forgotten.
- **Decorators over each use case's interface.** They hold the cross-cutting steps once, but need a
  decoration per interface or a container that scans for them at run time.
- **MediatR.** The same pipeline, with a commercial licence and dispatch resolved at run time.

## Consequences

- The compiler checks the wiring: a host's generated mediator names every message, and a message
  without exactly one handler stops the build. `MessageRoutingTests` reads the same rule off the
  application layer and, where the API serves every operation, holds that a host or a job sends each
  message.
- `UseCasePipelineTests` holds the order of the behaviours and that a message its validator refuses
  never reaches its handler; `ContainerCompositionTests` holds that every message with a validator
  passes through the validation behaviour in the composed API.
- `UseCaseConventionTests` holds the shape of a use case's folder and `LayoutConventionTests` the pair
  its own file holds. `LayerDependencyTests` refuses MediatR in every project and Mediator in the
  domain and the infrastructure, which neither send nor handle a message.
- `StorageVocabularyTests` holds the four words; `PortConventionTests` holds the six-operation
  ceiling and requires every port to have a consumer in the application layer; `StorageShapeTests`
  refuses an `IQueryable` on a port.
- Each new query is a named method on a port, with its own test, rather than an expression a caller
  writes.

[Ports and adapters](../architecture/ports-and-adapters.md) and
[Use cases are messages](../architecture/domain-and-results.md#use-cases-are-messages)
describe the vocabulary and its rules.

## Revisit when

A use case needs behaviour the pipeline cannot give every message alike, or a port's callers keep
needing query shapes no named method can anticipate.
