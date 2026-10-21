# Domain and results

An aggregate enforces its own rules, a use case reports an expected failure as a value, and a fact
reaches the rest of the system as a domain event once it is true. This page is the mechanics every
rich feature uses.

## Aggregates

`AggregateRoot<TId>`, from `AppTemplate.Domain.Core`, is the consistency and transaction boundary.
An aggregate whose row carries audit columns and a version derives from `AuditableAggregateRoot<TId>`,
which holds those values and implements `IAuditable` and `IVersioned` explicitly: the store writes
them, and application code holding the aggregate cannot.

Only a root gets a repository, and the entities inside it are reached through the root. A repository
loads the complete aggregate, because an invariant can only be checked against everything the root
owns. That makes the size of an aggregate a bound on the cost of every command, so a collection
inside one carries a cap. The version lives on the root only: a concurrent edit to anything inside
is a conflict on the root.

Time is a parameter, not an ambient dependency: a factory or a behaviour takes `now` with its data,
so the aggregate is reproducible in a test without freezing a clock. `DomainModelTests` holds the
rest of the shape: roots are sealed, entities expose no settable state, value objects and events are
immutable.

<!--#if (todoLists)-->
`TodoList` is the worked case. Item titles are unique within a list and a list holds at most
`TodoList.MaxItems` items, so the repository loads every item and tag with the list, and no route
reaches an item without naming its list.
<!--#endif-->
<!--#if (tagging)-->
A tag set belongs to its aggregate: the aggregate chooses the cap and owns the set, while the rules
every tagged aggregate shares, held once, sit in `TagSet` under the domain's `Common/Tagging/`.
<!--#endif-->

## Domain events after the commit

A root raises an event with `RaiseDomainEvent`, and the event waits on the aggregate. The dispatch
interceptor collects the events of the aggregates staged on the context being saved as the save
begins, and publishes them once that save has committed. An event is therefore never observed for a
transaction that rolled back; a failed save hands its events back, so the retry publishes them.

A fact no aggregate carries, such as an operation's own outcome, is published through
`IDomainEventPublisher` by the use case that made it true, after its own commit.

Consumers run in process, after the commit and outside its transaction. A consumer implements
`IDomainEventConsumer<TEvent>` and is registered with `AddDomainEventConsumer`; one that throws is
logged and skipped, so delivery is at most once. Every consumer therefore re-derives its
precondition when it runs and is idempotent by shape, and a sweep, not a retry, is the guarantee
behind a fact that must not be lost. `DomainEventTests` fails when an event is neither consumed nor
listed with the reason it is not.

## Result for expected failures, exceptions for bugs

An expected outcome is a value; only a bug is an exception.

| Situation | Mechanism | Answer |
|---|---|---|
| Not found, conflict, validation failure, not authenticated | `Result` or `Result<T>` carrying an `Error` | the status of its `ErrorType`, with its `code` |
| An invariant the use case expects the aggregate may refuse | the call wrapped in `DomainGuard.Try` | `409` `domain.invariantViolated` |
| An invariant violated where nothing expected it | `DomainException`, uncaught | `400` with a fixed message |
| A lost update at commit | `ConcurrencyConflictException` | `409` `concurrency.conflict` |
| A programming error, an infrastructure failure | any other exception | `500` with a `traceId` and no detail |

"Not found" is one of two normal answers to a lookup. As a `Result` it is visible in the use case's
signature; thrown, it would surface as an unhandled fault and leak a message to the client.

An `Error` carries a stable dotted `Code`, a message and an `ErrorType`. Each feature declares its
codes in one `…Errors` class, so a situation never acquires two codes, and a code no feature owns
lives in `CommonErrors`. `ErrorMapping` is the one place a type becomes an HTTP status, so a situation
answers the same status on every endpoint; the codes themselves are tabled in
[the API reference](../reference/api.md#error-codes).

A use case catches only what its caller could not have avoided. The validator is the first line, so
a `DomainException` for something the validator already refused means the two disagree: a bug to
surface, not an outcome to map.

## Use cases, not a dispatcher

A use case is a sealed class with one `ExecuteAsync` method, behind one named interface, registered
by its feature's `Add<Feature>()` call and injected into the controller that calls it. There is no
handler type, no dispatcher and no pipeline behaviour: validation runs in the use case, logging is
`ILogger`, and the transaction boundary is `IUnitOfWork`. `LayerDependencyTests` forbids MediatR by
name.

A command reads the caller, validates, loads or creates, changes the aggregate, commits and maps:

```csharp
public async Task<Result<Versioned<WidgetDto>>> ExecuteAsync(
    CreateWidgetCommand command,
    CancellationToken cancellationToken = default)
{
    var userId = currentUser.RequireUserId();
    if (userId.IsFailure)
    {
        return userId.To<Versioned<WidgetDto>>();
    }

    var validation = await validator.EnsureValidAsync(command, cancellationToken);
    if (validation.IsFailure)
    {
        return validation.To<Versioned<WidgetDto>>();
    }

    var widget = Widget.Create(userId.Value, command.Name, clock.UtcNow);
    repository.Add(widget);
    await unitOfWork.SaveChangesAsync(cancellationToken);

    return WidgetDtoMapping.Detail(widget);
}
```

Reads and writes go through different ports: a repository loads aggregates to change them, and a
queries port projects rows straight onto DTOs, since a list view needs no invariant enforced.

## Decisions

- [0003. Use cases and ports are explicit](../decisions/0003-use-cases-and-ports-are-explicit.md)
- [0006. Events after commit without an outbox](../decisions/0006-events-after-commit-without-an-outbox.md)
- [0002. Two feature shapes](../decisions/0002-two-feature-shapes.md)
