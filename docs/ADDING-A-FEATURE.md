# Adding a feature

A worked walkthrough of the vertical `CONTRIBUTING.md`'s "Adding a feature" section
summarises: aggregate → EF model → mapper → tracker → repository → use case (with its
named interface) → controller → tests → migration. `TodoLists` is the real example
running through every step below — open the file next to the paragraph describing
it. `Reminders` is the same vertical without child entities, and `Files` is the one
whose state does not all live in the database; sections 4b and 4c say what each of
those changes.

The rule behind every step: **a layer only speaks its own language.** The domain
never imports EF; the API never imports EF Core either, only the application
layer's DTOs and ports. `Tests/Architecture/AppTemplate.Architecture.Tests` enforces
this, so a shortcut here is a failing architecture test rather than a silent drift.
Those rules are tests, not compiler checks: `dotnet build` says nothing about them,
so run `dotnet test Tests/Architecture` before you push. Where a step below can only
be got right by knowing which rule reads it, the rule is named on the spot.

Two placeholders run through the whole document. **`<Feature>` is the folder, in the
plural** — `TodoLists`, `Reminders`, `Files` — and **`<Aggregate>` is the type, in the
singular** — `TodoList`, `Reminder`, `StoredFile`. Folders under `Features/` carry the
feature; file and type names carry the aggregate. `TodoListErrors` lives in
`Features/TodoLists/Errors/`, and nothing in the tree is called `TodoListsErrors`.

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
`.Repositories` — that is the whole rule, so follow the folder name exactly. Seal the
aggregate root: `DomainModelTests.AggregateRoots_AreSealed` requires it.

**An event nothing listens to has to be a decision, not an oversight.** The first
`Events/<Thing>DomainEvent.cs` you add turns
`DomainEventTests.EveryDomainEvent_IsEitherConsumedOrListedAsUnconsumed` red until one
of two things is true: some `IDomainEventConsumer<T>` in the application layer handles
it, or its type name is written into the `_deliberatelyUnconsumed` array at the top of
`Tests/Architecture/AppTemplate.Architecture.Tests/Rules/DomainEventTests.cs` with the
reason nothing consumes it. Publishing a fact no consumer wants yet is perfectly good
design; leaving the reader unable to tell that from a consumer somebody forgot is not,
and the array is where the difference is stated.

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
Errors/<Aggregate>Errors.cs    the feature's Error catalogue
```

A folder is only present when it has content — a feature with no domain-event consumer
has no `Consumers/`. `Dtos/` holds only shapes more than one operation returns; a
response type only one operation produces stays in that operation's own folder instead.
A type that appears in a port's signature never moves into a use case's folder, however
many use cases call that port — otherwise `Ports/` would depend on `UseCases/`.
`TodoListPageRequest` is the real example: it is `ITodoListQueries`'s parameter, so it
lives in `Ports/TodoListQueries/`, not inside any one query's own folder.

**Two of those folders hold things nothing discovers for you, and both are registered
by hand in `ApplicationModule.AddApplicationLayer`.** A domain-event consumer is bound
to its event with `services.AddDomainEventConsumer<TEvent, TConsumer>()`; the binding
is written out rather than scanned, so a consumer nothing reaches is an absence you can
see in the file instead of a silence at runtime. A collaborator under `Services/` has
no request or response shape of its own, so the marker-based discovery below never sees
it either: bind it with its own `services.AddScoped<TContract, TImplementation>()` next
to the three that are already there. Skip either line and the code still compiles — the
consumer simply never fires, and the use case that takes the collaborator throws on its
first resolution.

A port is a capability, not a façade, and two architecture rules hold it to that.
`PortConventionTests` refuses any port declaring more than **four** operations: four is
what the widest port here needs, and a fifth means a second capability that belongs in
a port of its own. It also refuses a feature port that *every* use case in its vertical
depends on, because a vertical whose use cases are all wrappers around one collaborator
has put its logic in the collaborator. Neither rule applies to the cross-cutting ports
in `Common/Ports/` — the clock and the caller's identity may legitimately be
needed everywhere.

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

The named interface is not decoration: `ApplicationModule.AddUseCasesFrom` walks the
application assembly and registers every `IUseCase` implementation under the **one**
other interface it declares. Zero or several named interfaces throws from
`AddApplicationLayer` itself, at registration, before the container is ever built —
rather than surfacing as a 500 on first request the way a missing registration would.
Controllers depend on the named interface, never on the concrete class.

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
Policies/<Aggregate>CollectionPolicy.cs              the sortable whitelist and the bounds
Ports/<Aggregate>Queries/<Aggregate>Filter.cs       the typed filter surface + its validation
Ports/<Aggregate>Queries/<Aggregate>PageRequest.cs  the one validated type the port accepts
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

    /// <summary>
    /// Offset-only because the column is nullable: a keyset comparison against a row
    /// whose key is <c>NULL</c> is neither true nor false, so the row the cursor was
    /// minted from would be skipped rather than resumed from.
    /// </summary>
    public const string LastModifiedAtField = "lastModifiedAt";

    public static readonly TodoListCollectionPolicy Instance = new();

    public IReadOnlyList<SortableField> SortableFields { get; } =
    [
        SortableField.Keyset(NameField),          // may also be used with paging=cursor
        SortableField.Keyset(CreatedAtField),
        SortableField.OffsetOnly(LastModifiedAtField),
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
single `<Aggregate>PageRequest` — which has no public constructor, so a request that
skipped validation cannot be built at all:

```csharp
Task<PagedResult<TodoListSummaryDto>> GetForOwnerAsync(
    Guid ownerId,
    TodoListPageRequest request,
    CancellationToken cancellationToken = default);
```

The translation to SQL is the persistence half, in
`Features/<Feature>/Queries/<Aggregate>SortMap.cs`: an exhaustive `switch` on the
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

Registration is in two files, and both are required. The first is
`PersistenceModule.AddPersistenceModule`, which wires the container — add an
`Add<Feature>Feature` method of your own next to the existing `AddTodoListsFeature`,
and call it from `AddPersistenceModule` alongside the others:

```csharp
services.TryAddSingleton<ITodoListMapper, TodoListMapper>();

services.TryAddScoped<TodoListTracker>();
services.TryAddScoped<ITodoListTracker>(p => p.GetRequiredService<TodoListTracker>());
services.AddScoped<IAggregateFlusher>(p => p.GetRequiredService<TodoListTracker>());
services.AddScoped<IDomainEventSource>(p => p.GetRequiredService<TodoListTracker>());

services.TryAddScoped<ITodoListRepository, TodoListRepository>();
services.TryAddScoped<ITodoListQueries, TodoListQueries>();
```

That last line is the read-side port from section 2, and it is the one to check twice:
leaving it out breaks nothing the write endpoints touch, so everything looks right
until the list endpoint fails to resolve on its first request.

**Register the tracker once, as a factory under each contract — never three
separate `AddScoped` calls.** Three registrations mean three instances: the
repository fills one identity map, the flush interceptor flushes a different, empty
one, and every write persists nothing, silently, with no error anywhere.
`SharedInstanceRegistrationTests` exists to catch exactly this, and it will fail
loudly if a new feature gets it wrong — do not treat a failure there as a false
positive.

The second file is
`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs`,
and it is where the mapping becomes real. **`PersistenceModule` applies no EF
configuration at all**; `AppDbContext` names each one by hand, so a feature needs three
things written there:

1. **A schema constant.** No default schema is set on the model, so a table that names
   none lands in the connection's default schema rather than the feature's. Add
   `public const string <Feature>Schema = "<feature>";` beside `TodoSchema`,
   `RemindersSchema` and `FilesSchema`, and read it from your
   `IEntityTypeConfiguration`'s `ToTable` call. A feature that owns its own schema is
   also a feature whose removal is a deleted migration file rather than a drop.
2. **A `DbSet`.** `internal DbSet<<Aggregate>Record> <Feature> => Set<<Aggregate>Record>();`
   — internal, like every other set here, because nothing outside the assembly has any
   business naming a storage shape.
3. **The `ApplyConfiguration` call.** Add
   `builder.ApplyConfiguration(new <Aggregate>RecordConfiguration());` to
   `OnModelCreating`, in the block for your feature.

Miss the third and the configuration is inert. Nothing about that is loud on its own:
the build is green, the architecture rules that read types are green, and
`dotnet ef migrations add` writes an **empty** migration, because the model and the
snapshot omit the entity in exactly the same way — so even `PendingModelChangesTests`
agrees. The first thing that disagrees is a query at runtime.
`PersistenceModelTests.EveryEntityTypeConfiguration_IsAppliedByTheContext` is the rule
that closes that gap: it reads the `IEntityTypeConfiguration` implementations in the
module, reads the `ApplyConfiguration` calls out of `AppDbContext.cs`, and names any
configuration that appears in the first list and not the second. Its counterpart
checks the other direction. Run the architecture project and you will be told which
configuration you forgot; skip it and you will find out from a `500`.

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

Two things about the HTTP surface are decided for you, and `HttpSurfaceTests` holds
both. **There is no `PATCH`**: a partial update means an omitted field is ambiguous
between *absent* and *unchanged*, and an invariant is a property of the whole
aggregate, so it cannot be checked against a body whose meaning depends on which keys
the client happened to send. Every write says in its route what it does — `RenameX`,
`CompleteX`, `RescheduleX` — rather than accepting a bag of optional fields.
**And an `[AllowAnonymous]` is two edits, not one**: the attribute on the action, and
the action's method name added to the `_anonymousActions` array in
`Tests/Architecture/AppTemplate.Architecture.Tests/Rules/HttpSurfaceTests.cs`. That
array is the whole of the public surface written down in one place, so opening an
endpoint is a line somebody reviews rather than an attribute nobody notices.

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
follows the same nature-word vocabulary as everything else: `Common/{Budgets,
Factories, Options}` and `Features/Files/{Inspectors, Inventories, Options, Scanners,
Stores}`, one plural per nature of file and nothing else inside. A new infrastructure
module of your own gets the same treatment, and
`LayoutConventionTests.EveryInfrastructureModuleOnDisk_HasAVocabularyOfItsOwn` will not
let it build until its vocabulary is written into `CONTRIBUTING.md`'s layout tree and
into the rule's own dictionary.

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

## 4c. When the aggregate has no child entities — the `Reminders` example

`TodoLists` owns items, and items own tags, so a good part of what section 3 asks for
exists only because of that ownership. `Reminders` is the same vertical with a flat
aggregate, and it is the closer comparison for most new features: read it to see which
of the pieces above you actually need.

What it does not have is the point. The domain side is four files —
`Entities/Reminder.cs`, `ValueObjects/ReminderState.cs`,
`Events/ReminderFiredDomainEvent.cs`, `Repositories/IReminderRepository.cs` — with no
child collection, so `ReminderRepository` loads a row and maps it, where
`TodoListRepository` needs `.Include(list => list.Items).ThenInclude(item => item.Tags)`
and `.AsSplitQuery()`. There is no `Policies/` folder either: reminders are read one
item's worth at a time, so the feature has no collection endpoint, and therefore no
`CollectionPolicy`, no `Filter`, no `PageRequest` and no `SortMap`. Section 2b simply
does not apply to it. Skipping all of that is the default, not a shortcut — build the
collection machinery when a feature has a list to page, and not before.

Three things it does show that `TodoLists` does not. **The four-operation cap is a rule
about application ports, not about repository contracts.** `IReminderRepository`
declares five members, and that is legitimate: it lives in the domain, and
`PortConventionTests` reads only interfaces under
`AppTemplate.Application.Features.<F>.Ports`. **`ReminderFiredDomainEvent` is a live
entry in `_deliberatelyUnconsumed`**, with the reason written next to it — the worked
example of the decision section 1 asks you to make. **And the feature is driven from
two hosts**: `RemindersController` serves the HTTP side, while `FireDueRemindersUseCase`
is called by the Worker, which is why the feature has ports for a notifier
(`Ports/ReminderNotifier/`), for its own metrics (`Ports/ReminderDiagnostics/`) and for
the targets it notifies (`Ports/ReminderTargetQueries/`). A use case is a use case
whichever host calls it; nothing about the shape changes.

## 5. Tests

`Tests/` mirrors `Src/`: domain tests under `Tests/Domain/AppTemplate.Domain.UnitTests/`,
application tests under `Tests/Application/AppTemplate.Application.UnitTests/`, and so
on. The mirror is exact down to `Features/<Feature>/`; below that it is followed only
where a feature has enough tests of one nature to be worth separating. In the
persistence mirror, for instance, the mapper and tracker tests sit directly under
`Features/<Feature>/` while `Queries/` has a folder of its own. Match the neighbouring
feature rather than the source tree when the two disagree. Add:

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
dotnet run Tools/Tasks.cs migration-add <Name>
```

Then confirm nothing was missed:

```bash
dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
```

`PendingModelChangesTests` asserts the same thing without a database and runs in CI, so
a migration that has fallen behind the model is caught whether or not anyone ran the
command. Read it together with the caution in section 3: it compares the model to the
snapshot, and a configuration that was never applied is missing from both, so a green
result here is not on its own evidence that your table exists.

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

If you want it gone anyway, follow `docs/REMOVING-THE-EXAMPLE-FEATURES.md` rather than
improvising from the tree. Deleting the folders is the easy half. The removal takes
`Reminders` with it, because a reminder is scheduled against a to-do item and that
feature does not compile without this one; it touches `ApplicationModule` in five
separate lines, one of which is the validator anchor naming a type you just deleted;
and the migration is where guessing costs data, since `AddExampleFeatures` carries
`todo` and `reminders` together and what to do with it depends on whether a database
has already applied it. That document names every file, every edit and the state each
test suite is left in.
