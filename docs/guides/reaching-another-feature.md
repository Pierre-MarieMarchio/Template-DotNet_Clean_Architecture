---
type: guide
title: Reaching another feature
description: How one feature reads, changes or reacts to another through a port or a domain event, so that each stays removable.
---

# Reaching another feature

## When

A feature needs another one: to read its state, to change it, or to let it react to a fact. At the
end, the two meet through a port or a domain event, the only ways
[ADR 0034](../decisions/0034-features-meet-through-ports-their-owner-declares-and-events.md) allows, and each feature
can still be removed without editing the other's internals.

| The feature needs to | Through | Runs |
|---|---|---|
| read another's state | a read port the other declares and implements | inside its use case, before it decides |
| change another's state as part of its own operation | a write port the other declares and implements | inside its use case, committed with it |
| let others react to what it did | a domain event it raises, and a consumer in each feature that reacts | after its commit, at most once |

A port is a synchronous call whose answer the use case needs. An event is a fact the raising
feature does not wait on: if nobody needs the answer, it is an event.

## A read port

The feature that owns the data declares the port in its own application folder, under
`Ports/<Port>/`, and decides what it exposes; the persistence module implements it, under that
feature's `Access/Queries/`. The feature that reads takes the port in its handler, and its tests
substitute it.
<!--#if (reminders)-->
`ITodoItemQueries` is the worked case: TodoLists declares it in
`Src/Application/AT.Application/Features/TodoLists/Ports/TodoItemQueries/`, the persistence module
implements it as `TodoItemQueries`, and the Reminders handlers ask it whether an item exists for its
owner and whether it is done, never reading the to-do rows themselves.
<!--#endif-->

## A write port

Feature `<A>` changes state that feature `<B>` owns, and the change belongs to `<A>`'s operation:
both commit, or neither does.

1. Declare the port in `<B>`'s application folder,
   `Src/Application/AT.Application/Features/<B>/Ports/<Capability>/I<Capability>.cs`, as
   [ADR 0034](../decisions/0034-features-meet-through-ports-their-owner-declares-and-events.md) says: the port belongs to
   the feature that owns the state. It answers a `<Capability>Status` enum beside it that closes the
   ways the call can go, never a `Result`: the use case that calls it chooses the error code.
<!--#if (accounts)-->
   It takes the owner, so another caller's record answers as a missing one
   ([ADR 0009](../decisions/0009-ownership-is-in-the-query.md)).
<!--#endif-->
2. Implement it in `<B>`'s `Services/` folder, as `<Capability>Service`, internal: it loads `<B>`'s
   aggregate through `<B>`'s repository and calls the aggregate method that holds the rule, so `<B>`'s
   invariants still guard its state. It stages and does not commit; a rule the aggregate may refuse
   becomes a value of the status. A feature of the simple shape, which has no aggregate, stages
   through its store instead, after checking its input policy.
3. Register it in `Add<B>` of `ApplicationModule`, beside `<B>`'s other services:
   `services.AddScoped<I<Capability>, <Capability>Service>();`. A host that composes `<A>` then
   needs `Add<B>` too, as a read port does.
4. Take the port in `<A>`'s handler, call it, map each status to one of `<A>`'s errors, then commit
   through `IUnitOfWork`: both features' rows live in the business context, so that one commit saves
   both, and the events `<B>`'s aggregate raised are published after it.
5. Test `<B>`'s service with substitutes of `<B>`'s repository, one test per status; test `<A>`'s
   handler with a substitute of the port, including a status that refuses and leaves nothing
   committed.

When several features each answer the same request, the port is declared once, in
`AppTemplate.Application.Core`, and each owner implements it.
<!--#if (anyFeature)-->
`IDeletedAccountDataPolicy` is the worked case, which every feature that owns user data implements
and the account-deletion sweep calls.
<!--#endif-->

## A domain event

Feature `<A>` announces a fact; any feature, `<A>` included, reacts after the commit
([ADR 0006](../decisions/0006-events-after-commit-without-an-outbox.md)).

### Raise it

1. Write the event and its consumer, or its entry among the unconsumed, with
   [`add event`](adding-a-feature.md#add-a-domain-event), which needs a feature of the rich shape:

   ```bash
   dotnet cleanarch add event <A> <Fact>DomainEvent --consumer <B>:<Reaction>
   ```

2. Raise it in the aggregate method that makes the fact true, after the method has changed the
   state, with `RaiseDomainEvent(new <Fact>DomainEvent(...))`; the event carries what a consumer
   needs to find what it reacts to, and `OccurredOn` from the time the method was given. The use case
   only commits: the save collects the events of the aggregates it stages, and publishes them once it
   has committed.
3. A fact no aggregate carries, an operation's own outcome, is published by the use case that made
   it true, through `IDomainEventPublisher`, after its own commit.

### Consume it

The consumer `add event` writes throws `NotImplementedException` until its body is written. The body
re-derives its precondition: it loads what it changes, decides from the current state whether
anything is left to do, does it, and commits a transaction of its own, since it runs after the
raising one. Running it twice is then running it once.
<!--#if (reminders)-->
`CancelRemindersOnTodoItemCompletedConsumer` is the worked case: it loads the reminders of the
completed item, keeps the pending ones, and commits only when one is left to cancel.
<!--#endif-->

### Leave it unconsumed

An event nobody reacts to yet is listed, with its reason, in `_deliberatelyUnconsumed` of
`Tests/Architecture/AT.Architecture.Tests/Rules/DomainEventTests.cs`; `add event --unconsumed`
writes the entry. `DomainEventTests` fails on an event that is neither consumed nor listed, and on an
event listed while something consumes it, so the first consumer of a listed event removes its entry.

### When a consumer fails

Delivery is at most once, in process, with no outbox and no retry. A consumer that throws is logged
at `Error`, naming the consumer and the event, and skipped; the other consumers of the event still
run, and the raising operation has already committed and answered. An event is also lost when the
process stops between the commit and the consumer. A reaction whose loss would matter therefore has a
sweep behind it: a job under a lease that re-derives the same effect from the stored state, so the
event only makes it prompt ([background jobs](../features/background-jobs.md)).
<!--#if (anyFeature)-->
The account-deletion sweep is the worked case, behind the consumers of `UserAccountDeleted`.
<!--#endif-->

### Test it

| What | Where | Proves |
|---|---|---|
| The event | the test `add event` writes beside the domain tests | what it carries |
| The raise | the aggregate's tests | the method raises it once, with the state it changed, and a refusal raises nothing |
| The reaction | the consumer's tests, with substitutes of its ports | it acts on the state it re-derives, and changes and commits nothing when nothing is left to do |
| The registration | the test `add event` writes beside the consumer | the consumer is registered for the event |
| <!--#if (api && !todoLists)--> |
| The delivery | an integration test that reads `Fixture.DomainEvents` | the operation reaches the consumer after the commit |
| <!--#endif--> |
| <!--#if (api && todoLists)--> |
| The delivery | an integration test that reads `Fixture.DomainEvents` | the operation reaches the consumer after the commit; `DomainEventDispatchTests` is the model |
<!--#endif-->

## Check

`dotnet test --project Tests/Architecture/AT.Architecture.Tests` passes. `FeatureIsolationTests`
refuses a reach past a port or an event, in the application and in the persistence projects;
`PortConventionTests` a port nobody calls; `ContainerCompositionTests` a port with no registration;
`DomainEventTests` an event neither consumed nor listed.

## Undo

Remove the port, its implementation, its registration and the calls, or the event, its raise, its
consumers and their registrations; an event that stays without a consumer goes back to
`_deliberatelyUnconsumed` with its reason.
