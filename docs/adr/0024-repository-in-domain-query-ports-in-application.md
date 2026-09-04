# 0024 — A repository contract lives in the Domain; every other port lives in Application

Status: Accepted

## Context

A use case that changes a `TodoList` and a use case that lists `TodoList`s for a page of
results both reach the database, through two contracts that want opposite things.

The first wants the aggregate: `TodoList`, with its items and tags loaded, so
`TodoList.AddItem` can check the invariants that only make sense with the whole aggregate in
hand — a duplicate title, the item cap ([0003](0003-aggregate-oriented-repository.md)). Its
contract can only be stated in domain types, because "load the aggregate" and "stage it for
insertion" have no other vocabulary:

```csharp
Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken);
void Add(TodoList todoList);
```

The second wants a page of rows projected straight into `TodoListSummaryDto` in SQL.
Loading the aggregate to answer it would mean reconstructing every item and tag from a value
object graph just to serialise it back out as columns — the cost [0003](0003-aggregate-oriented-repository.md) already rejected
by keeping reads on their own port. Its contract is stated in a DTO the aggregate has never
heard of:

```csharp
Task<PagedResult<TodoListSummaryDto>> GetForOwnerAsync(
    Guid ownerId, TodoListPageRequest request, CancellationToken cancellationToken = default);
```

Filing both under one word, in one layer, would force one of them to lie. Put the read
contract beside the write contract in the Domain and it has to pretend a `TodoListSummaryDto`
is something the domain model has a reason to know about. Put the write contract in
Application and it has to pretend an aggregate's own invariants are an application concern
rather than the domain's.

A third shape exists alongside these two: a contract that talks to storage with no aggregate
on either side of it. A refresh token is a credential the authentication adapter mints and
consumes — 32 CSPRNG bytes and a hash — not a concept with rules of its own. `AppUser`,
`AppRole` and `RefreshToken` are framework persistence types with no domain counterpart
([0011](0011-persistence-models-separate-from-the-domain.md)). There is no aggregate for a
contract like this to sit beside, in Domain or in Application, and inventing one to give it
somewhere to live would be the "domain user" 0011 already declines to create.

## Decision

**Where a contract is declared follows what it is expressed in.**

- **Repository.** An aggregate loaded, mutated through its own behaviour, and staged for a
  commit someone else owns. Declared in `AppTemplate.Domain`, under
  `Features/<Feature>/Repositories/` —
  `ITodoListRepository`
  (`Src/Domain/AppTemplate.Domain/Features/TodoLists/Repositories/ITodoListRepository.cs`). Its
  signature names only domain types, because an aggregate is the only thing it deals in.
- **Every other application port.** A read projected to DTOs, or a platform capability with no
  aggregate behind it — an email sender, a clock, a token issuer. Declared in
  `AppTemplate.Application`, under `Features/<Feature>/Ports/<Port>/` — `ITodoListQueries`
  (`Src/Application/AppTemplate.Application/Features/TodoLists/Ports/TodoListQueries/`).
- **Store.** Storage for something with no domain presence at all, not because it was left out
  of the domain but because no aggregate exists to put there. Declared beside its one
  implementation, in the infrastructure project that owns the table, and consumed directly by
  whichever module needs it — no Domain or Application project in between.
  `IRefreshTokenStore`
  (`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Identity/Stores/IRefreshTokenStore.cs`)
  is declared in Persistence and consumed from Infrastructure.Identity.

One word per notion. `Repository` always means "an aggregate lives behind this." `Store` always
means "nothing does, and that absence is the reason it isn't a Repository or a Port" — not a
gap to eventually fill with a domain entity.

## Consequences

- `ApplicationPorts_ArePublicInterfaces`
  (`Tests/Architecture/AppTemplate.Architecture.Tests/Rules/AdapterVisibilityTests.cs`) holds the
  Domain/Application split for the ports it knows about. It recognises a repository contract by
  its folder — a namespace ending in `.Repositories` (`IsRepositoryContract`) — and requires that
  one to sit in the Domain assembly; every other port in its list must sit in Application.
  Moving `ITodoListRepository` to Application, or `ITodoListQueries` into a `Repositories/`
  folder in the Domain, fails the build.
- That guarantee is narrower than "this contract is in the right place." The test checks that a
  port's *folder name* agrees with the *assembly* it's declared in — it is a self-consistency
  check on the convention, not a review of any one contract's design. It would not catch a query
  port renamed into a `Repositories/` folder and moved to Domain alongside it: the convention
  would still be internally consistent, and wrong. What it reliably catches is the two mistakes
  that actually happen — a repository contract drifting into Application, or an application port
  drifting into the Domain — because either one breaks the folder/assembly agreement without
  anyone having to notice by eye.
- `Store` contracts sit outside that test on purpose. `IRefreshTokenStore` is not in the test's
  port list, because it crosses no Domain/Application boundary for the rule to guard. Its safety
  is structural — one implementation, one consumer, no other module can reach it — not enforced
  by an assertion.
- A reviewer who sees a `Repositories/` folder knows the interface inside speaks only in
  aggregates, without opening the file. A reviewer who sees `Store` knows there's no aggregate to
  check an invariant against, and isn't meant to go looking for one.

## Alternatives rejected

- **One port folder for both repository and query contracts.** Whichever layer hosts it, the
  other kind has to either depend on a layer it shouldn't (Domain importing Application for a
  DTO reverses the dependency arrow) or use a vocabulary that doesn't match what it returns.
- **Calling the read port a `Repository` too**, since it also fetches persisted data. Rejected
  because `Repository` would then name two unrelated things — "loads and mutates an aggregate"
  and "projects rows to a DTO" — and stop telling a reader anything by itself.
- **Giving `RefreshToken` a domain entity so its contract could be a repository.** Manufactures
  an aggregate with no invariant of its own to justify the label, for the sole purpose of having
  somewhere conventional to put an interface.

## Revisit when

A second `Store` contract appears with a shape unlike `IRefreshTokenStore`'s. One example
generalises to nothing; "declared beside its one implementation" needs a real rule once there
are two.
