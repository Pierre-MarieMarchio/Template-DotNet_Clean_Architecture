# Adding a feature

A worked walkthrough of the vertical `CONTRIBUTING.md`'s "Adding a feature" section
summarises: aggregate → EF model → mapper → tracker → repository → use case (with its
named interface) → controller → tests → migration. `TodoLists` is the real example
running through every step below — open the file next to the paragraph describing
it. `Reminders` is the same vertical without child entities, and `Files` is the one
whose state does not all live in the database; section 4b says what that changes.

The rule behind every step: **a layer only speaks its own language.** The domain
never imports EF; the API never imports EF Core either, only the application
layer's DTOs and ports. `Tests/Architecture/AppTemplate.Architecture.Tests` enforces
this at build time, so a shortcut here is a failing test, not a silent drift.

## 1. Domain — the aggregate

`Src/Domain/AppTemplate.Domain/Features/<Feature>/`

```
Entities/<Aggregate>.cs         the aggregate root
ValueObjects/<Name>.cs          validated primitives (record types)
Events/<Thing>DomainEvent.cs    what the aggregate raised
Repositories/I<Aggregate>Repository.cs   the repository contract, in domain types only
```

`TodoList` (`Entities/TodoList.cs`) is the reference: a private list of items, a
factory (`Create`) and a rehydration path (`Rehydrate`) that both run the same
invariants — unique titles, an item cap, a non-empty owner — so a row loaded from
the database can never produce an aggregate that breaks its own rules. Put every
invariant in the constructor, the factory, *and* `Rehydrate`; missing one of the
three is exactly the bug `TodoListRehydrationTests` exists to catch.

The repository contract lives in `Repositories/`, not `Application`, because it is
stated entirely in domain types:

```csharp
public interface ITodoListRepository
{
    Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken);
    void Add(TodoList todoList);
    void Remove(TodoList todoList);
}
```

`AdapterVisibilityTests` recognises a repository contract by a namespace ending in
`.Repositories` — that is the whole rule, so follow the folder name exactly.

## 2. Application — the use case and its port

`Src/Application/AppTemplate.Application/Features/<Feature>/`

```
UseCases/Commands|Queries/<Operation>/         one folder per operation:
  <Operation>Command.cs                        the command or query record
  I<Operation>UseCase.cs                       the named interface
  <Operation>UseCase.cs                        the use case
  <Operation>CommandValidator.cs               FluentValidation, scoped to this operation
  <types used only by this one operation>      e.g. a response record no other operation returns
Ports/<Port>/                  a port interface, together with the messages that cross it
Consumers/<Event>/             domain-event consumers, if the feature has any
Services/                      internal collaborators shared by more than one use case
Policies/                      the collection whitelist, and every other rule the feature
                               enforces; each one named ...Policy
Extensions/                    small helpers scoped to this feature
Mapping/                       aggregate -> DTO projections
Dtos/<Name>Dto.cs              read models more than one operation shares
Errors/<Feature>Errors.cs      the feature's Error catalogue
```

A folder is only present when it has content — a feature with no domain-event consumer
has no `Consumers/`. `Dtos/` holds only shapes more than one operation returns; a
response type only one operation produces stays in that operation's own folder instead.
A type that appears in a port's signature never moves into a use case's folder, however
many use cases call that port — otherwise `Ports/` would depend on `UseCases/`.
`TodoListPageRequest` is the real example: it is `ITodoListQueries`'s parameter, so it
lives in `Ports/TodoListQueries/`, not inside any one query's own folder.

One class per use case, plus **exactly one named interface** deriving from
`IUseCase<TRequest, TResponse>` (or `IUseCase<TResponse>`):

```csharp
public sealed record CreateTodoListCommand(string Name);

public interface ICreateTodoListUseCase
    : IUseCase<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>;

public sealed class CreateTodoListUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<CreateTodoListCommand> validator) : ICreateTodoListUseCase
{
    public async Task<Result<Versioned<TodoListDetailDto>>> ExecuteAsync(
        CreateTodoListCommand command,
        CancellationToken cancellationToken = default)
    {
        // require the caller -> validate -> build the aggregate -> stage it ->
        // SaveChangesAsync -> project the written aggregate back to a DTO
    }
}
```

The named interface is not decoration: `ServiceRegistration.AddUseCases` scans every
`IUseCase` implementation and registers it under the **one** other interface it
declares. Zero or several named interfaces throws at container-build time — the
same moment a missing DI registration would otherwise surface as a 500 on first
request. Controllers depend on the named interface, never on the concrete class.

Return `Result`/`Result<T>` carrying an `Error` with a stable `code` for every
expected failure (not found, not owner, validation). Reserve `DomainException` for a
violated invariant, and either let it become a 400 through `GlobalExceptionHandler`
(a bare net, not a path any use case is meant to rely on), or run the call through
`DomainGuard` to turn it into a 409 `Result` when the use case *expects* the invariant
to sometimes refuse — never a 500: that status is for a bug nothing anticipated, not
for a caller driving an aggregate into a state the model forbids.

### If the feature has a collection endpoint

Sorting, filtering and paging are already built, once, in
`Common/Collections/`. A feature does not implement them — it **declares what it
allows**:

```
Policies/<Feature>CollectionPolicy.cs            the sortable whitelist and the bounds
Ports/<Feature>Queries/<Feature>Filter.cs        the typed filter surface + its validation
Ports/<Feature>Queries/<Feature>PageRequest.cs   the one validated type the port accepts
```

The filter and the page request travel with the read-side port they are the parameter
of — `TodoListFilter` and `TodoListPageRequest` live in `Ports/TodoListQueries/`, next to
`ITodoListQueries` itself — because they are that port's signature, not one use case's
private concern.

The policy is the whitelist, and it lives in its own `Policies/` folder rather than in
`Common/` because only the feature knows which of its columns are cheap to order by:

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
   fails a test instead of shipping. `CollectionContractTests.EveryCollectionPolicy_IsInternallyConsistent`
   in `Tests/Architecture/AppTemplate.Architecture.Tests` asserts exactly that, for
   every policy, automatically.
4. **The filter is typed, never a string that becomes a predicate.** Each filter
   is a named parameter with a CLR type and a validating factory returning
   `Result<T>`; free text goes through `SearchTerm`, which bounds its length. See
   `CONTRIBUTING.md` for why there is no expression language.

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
Mapping/I<Aggregate>Mapper.cs / <Aggregate>Mapper.cs
Tracking/I<Aggregate>Tracker.cs / <Aggregate>Tracker.cs
Repositories/<Aggregate>Repository.cs        implements the domain's repository contract
Queries/<Aggregate>Queries.cs                read-side projections, if the feature has any
```

**The record is not the aggregate.** `TodoListRecord` is a settable class with no
rules; `TodoList` is the aggregate with all of them. This split is
the subject of `CONTRIBUTING.md`'s Persistence paragraph — read it before changing any of the four pieces below,
because they exist specifically to keep the split intact:

1. **Record** (`Models/`) — implements `IAuditable`; carries the EF-visible shape
   (foreign keys, the `xmin`-backed `Version` column, audit stamps) and nothing the
   domain does not also need to persist.
2. **Configuration** (`Configurations/`) — `ToTable`, keys, indexes, string lengths
   read from the domain's own constants (e.g. `TodoListName.MaxLength`) so the
   column and the invariant cannot drift apart.
3. **Mapper** (`Mapping/`) — the one place that knows both shapes: `ToAggregate`
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
Contracts/Requests/<Verb><Aggregate>Request.cs    request records the controller binds
Contracts/Responses/<Verb><Aggregate>Response.cs  response records the controller returns
Mapping/<Feature>Mapping.cs                       request/response <-> application DTO
```

The controller depends on named use-case interfaces only, maps requests to
commands, and turns `Result`/`Error` into the right status via `ErrorMapping` (see
`ApiControllerBase`). Every endpoint is authenticated by default — `Program.cs`
installs a default-deny fallback policy — so an anonymous endpoint needs an
explicit `[AllowAnonymous]`, not the absence of an attribute.

## 4b. When the feature is not only rows — the `Files` example

Everything above assumes a feature whose whole state is rows in the one database.
`Files` is the third worked example and the one that breaks that assumption: half of
a stored file is an aggregate in PostgreSQL, the other half is bytes in an
S3-compatible object store. Read it when your feature has a second place where its
state lives — a bucket, a search index, a queue.

Five things it does differently, and none of them is optional once your feature has
two stores:

**The second store is behind a port, and the port speaks no domain type.**
`IFileContentStore` (`Src/Application/AppTemplate.Application/Features/Files/Ports/FileContentStore/`)
takes a `string` key and a `long` size, never an `ObjectKey` or a `FileSize` —
`StorageVocabularyTests` refuses a `Store` whose signature names anything under
`AppTemplate.Domain.Features`, and that refusal is what the word `Store` means here.
The adapter lives in its own module, `AppTemplate.Infrastructure.Storage`, which
names its folders by subject because it has one kind of adapter.

**The two halves meet in a use case and nowhere else.** No repository reaches the
bucket, no adapter reaches the database. `ConfirmFileUploadUseCase` is where the row
and the object are compared, and it is the only place either knows the other exists.

**Bytes do not travel through the API, in either direction.** Registering returns a
signed upload grant and the client writes to the store directly; reading returns a
signed download grant and the controller answers `302`. That is arithmetic rather
than taste: `RequestLimits:MaxRequestBodyBytes` is 65 536, and `IdempotencyFilter`
buffers and SHA-256s the entire body of every `POST`.

**Consistency between the two stores is re-derived, not transacted.** There is no
distributed transaction and no tombstone: `DELETE` removes the row, and
`ReclaimOrphanedContentUseCase` deletes every object no row references. That shape —
an effect that re-derives its own precondition — is what lets this template keep
refusing an outbox, and any feature with a second store owes the same shape. The
deletion event is a fast path on top of it, never the guarantee.

**Anything the second store holds is untrusted until something has looked at it.**
`InspectDepositedFilesUseCase` checks the declared media type against the bytes and
scans the content before a file becomes readable, in the worker rather than in the
request. See `SECURITY.md` for which threats that answers and which it does not.

The cost is a state machine, which a single-store feature does not need: `Pending →
Deposited → Available`, plus `Quarantined`. Its invariants are in the constructor,
the factory **and** `Rehydrate`, like any aggregate here — and its enum values are
pinned by a test, because they are persisted as integers and renumbering them would
reinterpret every existing row without changing a single line of the schema.

## 5. Tests

`Tests/` mirrors `Src/` one directory for one directory — domain tests under
`Tests/Domain/AppTemplate.Domain.UnitTests/`, application tests under
`Tests/Application/AppTemplate.Application.UnitTests/`, and so on. Add:

- Domain: the aggregate's invariants, from the constructor, the factory, and
  `Rehydrate` alike.
- Application: one test class per use case, plus the validators.
- Persistence: a round-trip fidelity test for the mapper (`ToNewRecord` composed
  with `ToAggregate` must reproduce every property), and a tracker test.
- Presentation (`Tests/Presentation/AppTemplate.Api.UnitTests/Features/<Feature>/`): the
  controller and the request/response mapping, against test doubles.
- Integration (`Tests/Integration/AppTemplate.Api.IntegrationTests/<Feature>/`): the
  HTTP surface end to end, against a real PostgreSQL via Testcontainers.
- If the feature has a second store (section 4b), it needs integration tests against
  the real thing too. A signature is computed locally, so a wrong one is
  indistinguishable from a right one until something checks it —
  `Tests/Integration/AppTemplate.Api.IntegrationTests/Storage/` is the worked example,
  against MinIO under Testcontainers.

The round-trip fidelity test deserves a second mention when a feature has a second
store, because it stops being hygiene. With no tombstone, a mapper that writes a key
differing from the one the bytes went to turns a storage leak into **data loss**: the
sweep finds the object unreferenced and deletes a live file. `StoredFileMapperObjectKeyTests`
is that barrier, and the mapper says so in its own file.

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

See `CONTRIBUTING.md` for why the API applies migrations at startup only in
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
`Tests/{Domain,Application,Infrastructure,Presentation}/**/Features/TodoLists/`,
`Tests/Integration/AppTemplate.Api.IntegrationTests/TodoLists/`, the
`AddTodoListsFeature` call in `PersistenceModule`, the `TodoItemCompletedDomainEvent`
consumer registration in `ServiceRegistration`, and the `TodoLists` table migration
(regenerate with `./tasks.ps1 migration-add InitialCreate` against a project that no
longer references the feature). Expect to also rewrite the integration tests listed
above against whatever resource replaces it.
