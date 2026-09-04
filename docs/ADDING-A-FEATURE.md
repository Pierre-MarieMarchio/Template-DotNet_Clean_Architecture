# Adding a feature

A worked walkthrough of the vertical `CONTRIBUTING.md`'s "Adding a feature" section
summarises: aggregate → EF model → mapper → tracker → store → use case (with its
named interface) → controller → tests → migration. `TodoLists` is the real example
running through every step below — open the file next to the paragraph describing
it.

The rule behind every step: **a layer only speaks its own language.** The domain
never imports EF; the API never imports EF Core either, only the application
layer's DTOs and ports. `Tests/Architecture/AppTemplate.Architecture.Tests` enforces
this at build time, so a shortcut here is a failing test, not a silent drift.

## 1. Domain — the aggregate

`Src/Domain/AppTemplate.Domain/Features/<Feature>/`

```
Entities/<Aggregate>.cs        the aggregate root
ValueObjects/<Name>.cs         validated primitives (record types)
Events/<Thing>DomainEvent.cs   what the aggregate raised
Stores/I<Aggregate>Repository.cs   the store contract, in domain types only
```

`TodoList` (`Entities/TodoList.cs`) is the reference: a private list of items, a
factory (`Create`) and a rehydration path (`Rehydrate`) that both run the same
invariants — unique titles, an item cap, a non-empty owner — so a row loaded from
the database can never produce an aggregate that breaks its own rules. Put every
invariant in the constructor, the factory, *and* `Rehydrate`; missing one of the
three is exactly the bug `TodoListRehydrationTests` exists to catch.

The store contract lives in `Stores/`, not `Application`, because it is stated
entirely in domain types:

```csharp
public interface ITodoListRepository
{
    Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken);
    void Add(TodoList todoList);
    void Remove(TodoList todoList);
}
```

`AdapterVisibilityTests` recognises a store by a namespace ending in `.Stores` —
that is the whole rule, so follow the folder name exactly.

## 2. Application — the use case and its port

`Src/Application/AppTemplate.Application/Features/<Feature>/`

```
UseCases/Commands|Queries/<Verb><Aggregate>UseCase.cs   command/query record, named
                                                          interface, and the use case
Dtos/<Name>Dto.cs             read models returned to the API
Ports/I<Thing>.cs             any port that is not a store (e.g. read-side queries)
Validators/<Verb><Aggregate>CommandValidator.cs   FluentValidation, one file each
Errors/<Feature>Errors.cs     the feature's Error catalogue
```

One class per use case, plus **exactly one named interface** deriving from
`IUseCase<TRequest, TResponse>` (or `IUseCase<TResponse>`):

```csharp
public sealed record CreateTodoListCommand(string Name);

public interface ICreateTodoListUseCase : IUseCase<CreateTodoListCommand, Result<Guid>>;

public sealed class CreateTodoListUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<CreateTodoListCommand> validator) : ICreateTodoListUseCase
{
    public async Task<Result<Guid>> ExecuteAsync(
        CreateTodoListCommand command,
        CancellationToken cancellationToken = default)
    {
        // validate -> load/build the aggregate -> stage it -> SaveChangesAsync -> return Result
    }
}
```

The named interface is not decoration: `ServiceRegistration.AddUseCases` scans every
`IUseCase` implementation and registers it under the **one** other interface it
declares. Zero or several named interfaces throws at container-build time — the
same moment a missing DI registration would otherwise surface as a 500 on first
request. Controllers depend on the named interface, never on the concrete class.

Return `Result`/`Result<T>` carrying an `Error` with a stable `code` for every
expected failure (not found, not owner, validation). Reserve `DomainException` for
a violated invariant and let it become a 500 — that path means the domain and the
use case disagreed about what is allowed, which is a bug, not a client error.

### If the feature has a collection endpoint

Sorting, filtering and paging are already built, once, in
`Common/Collections/`. A feature does not implement them — it **declares what it
allows**, in `Features/<Feature>/Collections/`:

```
Collections/<Feature>CollectionPolicy.cs   the sortable whitelist and the bounds
Collections/<Feature>Filter.cs             the typed filter surface + its validation
Collections/<Feature>PageRequest.cs        the one validated type the port accepts
```

The policy is the whitelist, and it lives here rather than in `Common/` because
only the feature knows which of its columns are cheap to order by:

```csharp
public sealed class TodoListCollectionPolicy : ICollectionPolicy
{
    public const string NameField = "name";
    public const string CreatedAtField = "createdAt";

    public static readonly TodoListCollectionPolicy Instance = new();

    public IReadOnlyList<SortableField> SortableFields { get; } =
    [
        SortableField.Keyset(NameField),          // may also be used with paging=cursor
        SortableField.Keyset(CreatedAtField),
        SortableField.OffsetOnly(LastModifiedAtField),   // nullable column — see below
    ];

    public string DefaultSort => "createdAt:desc";
    public int MaxSortTerms => 3;
    public int MaxPageSize => 100;
    public int DefaultPageSize => 20;
}
```

Four rules, and they are the whole reason this is safe:

1. **A field on the whitelist gets an index.** Putting a name in
   `SortableFields` is a promise that ordering by it is cheap, so add a composite
   index `(<owner or tenant key>, <field>, Id)` in the feature's
   `IEntityTypeConfiguration` — ending in the tiebreaker, so both the sort and the
   keyset comparison are index-ordered. If you are not willing to add the index,
   the field does not belong on the list.
2. **`SortableField.Keyset` only for a non-nullable column.** A keyset comparison
   against `NULL` is neither true nor false, so the row the cursor was minted from
   would be skipped instead of resumed from. A nullable column is
   `SortableField.OffsetOnly`, and asking for it with `paging=cursor` is a `400`.
3. **`DefaultSort` is written in the caller's own syntax** and parsed by the same
   `SortOrder.Parse` that parses caller input — so a typo in a feature's default
   fails a test instead of shipping. `CollectionPolicyRules` in
   `Tests/Architecture/AppTemplate.Architecture.Tests` asserts exactly that, for
   every policy, automatically.
4. **The filter is typed, never a string that becomes a predicate.** Each filter
   is a named parameter with a CLR type and a validating factory returning
   `Result<T>`; free text goes through `SearchTerm`, which bounds its length. See
   `docs/adr/0015` for why there is no expression language.

The use case parses the raw query into the validated types and hands the port a
single `<Feature>PageRequest` — which has no public constructor, so a request that
skipped validation cannot be built at all:

```csharp
Task<PagedResult<TodoListSummaryDto>> GetForOwnerAsync(
    Guid ownerId,
    TodoListPageRequest request,
    CancellationToken cancellationToken = default);
```

The translation to SQL is the persistence half, in
`Features/<Feature>/Queries/<Feature>SortMap.cs`: an exhaustive `switch` on the
canonical field name returning a typed key selector, a `default` arm that throws
because the use case should already have refused, and a mandatory
`.ThenBy(record => record.Id)` on every order. Copy `TodoListSortMap` — the shape
is the point, and `Tests/Architecture` enforces the parts of it that can be
checked mechanically.

## 3. Persistence — model, mapping, tracker, repository

`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/<Feature>/`

```
Models/<Aggregate>Record.cs                  the EF row — no behaviour, no invariants
Configurations/<Aggregate>RecordConfiguration.cs   IEntityTypeConfiguration<T>
Mappers/I<Aggregate>Mapper.cs / <Aggregate>Mapper.cs
Tracking/I<Aggregate>Tracker.cs / <Aggregate>Tracker.cs
Repositories/<Aggregate>Repository.cs        implements the domain's store contract
Queries/<Aggregate>Queries.cs                read-side projections, if the feature has any
```

**The record is not the aggregate.** `TodoListRecord` is a settable class with no
rules; `TodoList` is the aggregate with all of them. This split is
`docs/adr/0011`'s subject — read it before changing any of the four pieces below,
because they exist specifically to keep the split intact:

1. **Record** (`Models/`) — implements `IAuditable`; carries the EF-visible shape
   (foreign keys, the `xmin`-backed `Version` column, audit stamps) and nothing the
   domain does not also need to persist.
2. **Configuration** (`Configurations/`) — `ToTable`, keys, indexes, string lengths
   read from the domain's own constants (e.g. `TodoListName.MaxLength`) so the
   column and the invariant cannot drift apart.
3. **Mapper** (`Mappers/`) — the one place that knows both shapes: `ToAggregate`
   (row → domain, total), `ToNewRecord` (domain → row, total, exercised by a
   round-trip fidelity test), and `WriteTo` (domain → row, deliberately *partial* —
   it never touches the columns the store owns: audit stamps, the concurrency
   token). Stateless, EF-free, registered as a singleton.
4. **Tracker** (`Tracking/`) — the identity map EF is not maintaining, because EF
   only sees records, not aggregates. One instance per request resolved under
   **three** contracts (`I<Aggregate>Tracker` for the repository,
   `IAggregateFlusher` for the flush interceptor, `IDomainEventSource` for the
   dispatch interceptor) — see the registration note below, it is the single most
   common way to get this feature wrong.
5. **Repository** (`Repositories/`) — implements the domain's `I<Aggregate>Repository`.
   Consults the tracker first (identity map), then queries with
   `.Include(...).AsSplitQuery()` for anything with child collections, maps, and
   registers the result with the tracker. Never calls `SaveChangesAsync` — that is
   `IUnitOfWork`'s job, called once by the use case.

Register everything in `PersistenceModule.AddPersistenceModule`, following the
existing `AddTodoListsFeature` method as the template:

```csharp
services.TryAddSingleton<ITodoListMapper, TodoListMapper>();

services.TryAddScoped<TodoListTracker>();
services.TryAddScoped<ITodoListTracker>(p => p.GetRequiredService<TodoListTracker>());
services.AddScoped<IAggregateFlusher>(p => p.GetRequiredService<TodoListTracker>());
services.AddScoped<IDomainEventSource>(p => p.GetRequiredService<TodoListTracker>());

services.TryAddScoped<ITodoListRepository, TodoListRepository>();
```

**Register the tracker once, as a factory under each contract — never three
separate `AddScoped` calls.** Three registrations mean three instances: the
repository fills one identity map, the flush interceptor flushes a different, empty
one, and every write persists nothing, silently, with no error anywhere.
`SharedInstanceRegistrationTests` exists to catch exactly this, and it will fail
loudly if a new feature gets it wrong — do not treat a failure there as a false
positive.

## 4. API — controller and contracts

`Src/Presentation/AppTemplate.Api/Features/<Feature>/`

```
Controllers/<Feature>Controller.cs
Contracts/<Verb><Aggregate>Request.cs   request records the controller binds
```

The controller depends on named use-case interfaces only, maps requests to
commands, and turns `Result`/`Error` into the right status via `ErrorResults` (see
`ApiControllerBase`). Every endpoint is authenticated by default — `Program.cs`
installs a default-deny fallback policy — so an anonymous endpoint needs an
explicit `[AllowAnonymous]`, not the absence of an attribute.

## 5. Tests

`Tests/` mirrors `Src/` one directory for one directory — domain tests under
`Tests/Domain/AppTemplate.Domain.UnitTests/`, application tests under
`Tests/Application/AppTemplate.Application.UnitTests/`, and so on. Add:

- Domain: the aggregate's invariants, from the constructor, the factory, and
  `Rehydrate` alike.
- Application: one test class per use case, plus the validators.
- Persistence: a round-trip fidelity test for the mapper (`ToNewRecord` composed
  with `ToAggregate` must reproduce every property), and a tracker test.
- Integration (`Tests/Integration/AppTemplate.Api.IntegrationTests/<Feature>/`): the
  HTTP surface end to end, against a real PostgreSQL via Testcontainers.

A test that cannot fail is not a guarantee. For anything security- or
correctness-shaped: break the production code, watch the new test go red, then
restore it.

## 6. Migration

```bash
./tasks.ps1 migration-add <Name>
```

Then confirm nothing was missed:

```bash
dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
```

See `docs/adr/0009` for why the API applies migrations at startup only in
Development, and `SECURITY.md` for what a real deployment still has to do with the
migration bundle.

## Removing the sample instead

The `TodoLists` vertical is the worked example above, not a placeholder to delete
on day one — several integration tests (`Security/`, `TodoLists/`) use its
endpoints as their test subject for cross-cutting concerns (ownership isolation,
rate limiting, conditional requests, auditing) that have nothing to do with to-do
lists themselves. There is deliberately no `dotnet new` switch that strips it: doing
so cleanly would mean rewriting those tests against a different resource, not just
deleting files, and shipping a switch that quietly weakens the test suite is worse
than not shipping one.

If you want it gone, delete, in this order, and let the compiler and the
architecture tests find what is left: `Src/**/Features/TodoLists/`,
`Tests/{Domain,Application,Infrastructure}/**/Features/TodoLists/`,
`Tests/Integration/AppTemplate.Api.IntegrationTests/TodoLists/`, the
`AddTodoListsFeature` call in `PersistenceModule`, the `TodoItemCompletedDomainEvent`
consumer registration in `ServiceRegistration`, and the `TodoLists` table migration
(regenerate with `./tasks.ps1 migration-add InitialCreate` against a project that no
longer references the feature). Expect to also rewrite the integration tests listed
above against whatever resource replaces it.
