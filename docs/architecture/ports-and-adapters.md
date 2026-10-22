# Ports and adapters

The application reaches storage and the outside world only through ports: interfaces it declares
and an infrastructure module implements. It can be read and tested without knowing that EF Core,
MailKit or an identity framework exist.

## Ports are named for intent

A port's name says what the caller needs, never what supplies it: `IEmailSender`, not `ISmtpClient`;
`IUnitOfWork`, not `IEfCoreRepository`.

Where a port is declared follows who needs it:

| Port | Declared in |
|---|---|
| An aggregate's repository | `AppTemplate.Domain`, under `Features/<Feature>/Repositories/`, since it speaks only domain types |
| A port one feature needs | `AppTemplate.Application`, under `Features/<Feature>/Ports/<Port>/` |
<!--#if (localAuth)-->
| A port of an authentication feature | `AppTemplate.Application.Auth`, under `Features/<Feature>/Ports/<Port>/` |
<!--#endif-->
| A port any feature may need | `AppTemplate.Application.Core`, in `Common/Ports/` or beside its subject |

The cross-cutting ports, and what implements each:

| Port | Adapter |
|---|---|
| `IUnitOfWork` | `EfUnitOfWork` over the business context |
| `IEmailSender` | `MailKitEmailSender` or `PostmarkEmailSender`, chosen by configuration |
| `ICurrentUser`, `ICurrentAccessToken` | the host, from the request's claims |
| `IDateTimeProvider` | `SystemDateTimeProvider` |
<!--#if (postgreSql)-->
| `ILeaderLease` | `LeaderLease`, a session-level advisory lock |
<!--#endif-->
<!--#if (sqlServer)-->
| `ILeaderLease` | `LeaderLease`, a session-owned `sp_getapplock` application lock |
<!--#endif-->
<!--#if (sqlite)-->
| `ILeaderLease` | `LeaderLease`, an in-process keyed semaphore |
<!--#endif-->
| `IIdempotencyStore` | `IdempotencyStore`, over the `platform.IdempotencyKeys` table |
| `ICacheStore` | `HybridCacheStore` |
| `IDomainEventPublisher` | `DomainEventPublisher` |
<!--#if (anyFeature)-->
| `IUserAccountDirectory` | the host, over the accounts it knows |
| `IDeletedAccountDataPolicy` | each feature that owns user data |
<!--#endif-->

A port and a use case answer differently. A port answers with what the adapter observed: an
`…Outcome` record, or a `…Status` enum that closes the set of ways the call can go. A use case
answers with a `Result`, and that is where one of those ways becomes an error with a `code`. No port
returns a `Result`, so the code a client reads is chosen by the layer that knows the operation.

## Repository, Queries, Store, Table

Four words name the four ways to reach storage, and each one is checked:

| Word | Does | Declared in |
|---|---|---|
| `Repository` | loads a whole aggregate and stages its addition or removal | the domain |
| `Queries` | projects rows onto DTOs, with no aggregate materialised | the application |
| `Store` | stages rows that no aggregate stands behind | the application |
| `Table` | row access to one table, for that module's own adapters; no use case knows it | the infrastructure module that owns the rows |

There is no `IRepository<T>`. A generic repository can offer only what makes sense for every entity,
which is CRUD, and CRUD is what an aggregate exists to hide. No port exposes `IQueryable`: a contract
that hands out a query tree has published the database instead of abstracting it. And no repository
saves: it stages, and the use case commits.

`StorageVocabularyTests` holds the four words: a store or a table naming an aggregate has become a
repository, and a repository declared outside the domain is a promise it cannot keep.
`NoApplicationPort_ExposesAQueryable` holds the last rule.

## One capability, six operations

A port is one intent an adapter can satisfy on its own, with at most six operations. A seventh
operation means a second intent and a split. The shape refused is the façade: one interface for
everything a feature needs, whose implementation then owns the sequencing the use cases should.

`PortConventionTests` holds three rules: no port wider than six operations, no feature port that every
use case of its feature depends on, and no port the application layer declares but never calls. A
`Table` is not a port and is not counted.

## Interfaces on every entry point

Every use case sits behind one named interface deriving from `IUseCase<TRequest, TResponse>`, and a
controller depends on that interface. Registration finds the use cases of a feature and throws at
start-up if one declares no named interface or several. Every service class has an interface too:
code depends on it and the container supplies the implementation.

```csharp
public interface ICreateWidgetUseCase
    : IUseCase<CreateWidgetCommand, Result<Versioned<WidgetDto>>>;

public sealed class WidgetsController(ICreateWidgetUseCase createWidget) : ApiControllerBase;
```

## Adapters stay internal

An adapter of an application port is `internal` to its module, and a host composes it by calling the
module's registration method, never by naming the type.
`AdapterVisibilityTests.Adapters_ImplementingAnApplicationPort_AreInternalToTheirModule` holds it,
and it is what lets two modules share a mechanism without either owning it.

What stays public is what a caller cannot delegate: the ports, the options classes bound from
configuration, the registration classes, and the base types a module derives from, such as
`AggregateTracker`.

## The in-memory module

`AppTemplate.Infrastructure.InMemory` holds doubles for the ports a test should not reach for real:
the mail relay, a clock that moves only when told to, and the outward-facing ports of the features
that have them. No host composes it. A test calls `AddInMemoryModule` after the modules it replaces,
and the module removes their registrations before adding its own, so a double never depends on being
registered last.

A double never pretends to fail; a test that needs a failing adapter substitutes one for the single
call it cares about. Recording doubles are public, so a test can read back what was sent.

## Decisions

- [0003. Use cases and ports are explicit](../decisions/0003-use-cases-and-ports-are-explicit.md)
