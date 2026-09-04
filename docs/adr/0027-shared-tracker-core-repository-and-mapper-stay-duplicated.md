# 0027 — A shared tracker core; the repository and the mapper stay duplicated

Status: Accepted

## Context

`TodoList` (an aggregate with item and tag children) and `Reminder` (a flat aggregate, no children)
are the first two aggregates this persistence layer has ever had to carry side by side. Before
`Reminder` existed there was nothing to compare `TodoListTracker`, `TodoListMapper` and
`TodoListRepository` against, so every line in them was, by definition, load-bearing for the one
case it had. With a second case in hand, some of those lines turn out to be the same line twice, and
some only look that way until you count what they actually do.

The measurement, taken before touching anything:

| | `TodoList` | `Reminder` | Difference |
|---|---:|---:|---:|
| Tracker | 248 lines | 170 lines | −31% |
| Mapper | 199 lines | 103 lines | −48% |
| Repository | 93 lines | 114 lines | **+23%** |

A shrinking tracker and a shrinking mapper are consistent with "the second one has less to do, and
most of what it does is the same shape as the first." A **growing** repository is not consistent
with that story at all — `ReminderRepository` does more, not less, and the question this record
exists to answer is which of the three actually demonstrates a shared abstraction and which only
looks similar from a distance.

## Decision

**Extract exactly what both trackers turned out to do identically, once placed side by side.
Extract one identical helper out of the mappers. Touch the repository and the rest of the mapper not
at all.**

### The tracker: an abstract `AggregateTracker<TAggregate, TRecord>`, `Common/Tracking/`

`TodoListTracker` and `ReminderTracker` agreed, line for line, on seven members: `Find`, `FindRecord`,
`Track`, `MarkRemoved`, `RefreshFromStore`, `DrainDomainEvents`, `Restore`, and the private
`TrackedAggregate` pairing. None of those seven reads a single property that is specific to
`TodoList` or to `Reminder` — they read an id, a version, four audit stamps, and a list of domain
events, all of which are already named by `IVersioned`, `IAuditable` and `AggregateRoot<Guid>`. That
is what "both cases demonstrate": not a resemblance, a proof that the code never depended on
anything else.

`AggregateTracker<TAggregate, TRecord>`
(`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Tracking/AggregateTracker.cs`) is
constrained accordingly:

```csharp
internal abstract class AggregateTracker<TAggregate, TRecord>(Func<TRecord, uint> version)
    : IAggregateFlusher, IDomainEventSource
    where TAggregate : AggregateRoot<Guid>, IVersioned, IAuditable
    where TRecord : class, IAuditable
```

`TodoListTracker` and `ReminderTracker` now derive from it, closing the two generic parameters and
supplying the one thing the constraint cannot express — the record's concurrency token has no shared
interface to read it through, so each subclass passes the one-line accessor: `record => record.Version`.

**`FlushTo` is declared here, abstract, and shared with nobody.** It is the one member where the two
trackers disagree: `TodoListTracker.FlushTo` reconciles item and tag rows and has to decide which
root to touch when only a child changed; `ReminderTracker.FlushTo` maps six scalar columns and stops.
Folding both into one method — behind a "has children" flag, or a strategy object standing in for
the different half — would produce a single method a reader has to read twice, once per branch, to
find the half that applies to the aggregate in front of them. Left as two nine-line and
thirty-line overrides, each is fully legible on its own, and the divergence between them is the most
informative three seconds a reader of both spends in this codebase: it is where an aggregate with
children stops looking like an aggregate without any.

### The mapper: one extracted helper, `StoredStamps.ApplyTo`, not a base class

`TodoListMapper.ToAggregate` and `ReminderMapper.ToAggregate` each ended with the same four lines —
set the version, set the created stamp, conditionally set the modified stamp, throw the same
`InvalidOperationException` with only the aggregate's name text differing. That block is genuine
duplication by the same test the tracker was held to: neither implementation depends on anything
`IVersioned`/`IAuditable` do not already name. It is now `StoredStamps.ApplyTo<TAggregate>`
(`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Mapping/StoredStamps.cs`), called
once from each mapper's `ToAggregate`.

**Nothing else in either mapper moved, and no base class was introduced for the mapper as a whole.**
`ToNewRecord` and `WriteTo` are exactly where `TodoListMapper` and `ReminderMapper` stop agreeing —
one reconciles two levels of child collections and reports whether it had to; the other assigns six
scalars and returns nothing. Splitting a mapper's behaviour across a base class and a subclass would
mean the reflection-driven round-trip fidelity test — the one guarantee this type of class exists
to make legible — could no longer be read whole in one file; a property the base class silently
forgot to carry would be exactly the failure mode this template was rescued from, made harder to see
by eye rather than easier.

### The repository: untouched, and this is the discipline the mission was checking

`ITodoListRepository` has three members. `IReminderRepository` has five: `GetAsync`, `Add` and
`Remove` line up with `TodoListRepository`'s three, but `GetForTodoItemAsync` and `GetDueAsync` have
no equivalent on the `TodoList` side at all — nothing to extract them alongside, because there is
nothing on the other side to compare them against. `Reminder`'s repository is **bigger**, not
smaller, which is the opposite of what a "shared repository base" would need to be true to be worth
building.

[0003](0003-aggregate-oriented-repository.md) already rejected the generic shape this would have to
take:

> Generic `IRepository<T>`. It can only offer what makes sense for every entity, which is CRUD — and
> CRUD is precisely what an aggregate exists to hide.

A base class under `GetAsync`/`Add`/`Remove` would offer exactly that CRUD triplet and nothing else —
`GetForTodoItemAsync` and `GetDueAsync` could not live in it, because a base class cannot predict the
query the next aggregate's repository will need. The three-line saving on the three methods that do
line up would buy a base class with no home for the two that do not, which is a worse shape than the
duplication it would replace.

## Consequences

- **Measured, after:**

  | | Before | After | |
  |---|---:|---:|---|
  | `TodoListTracker` | 248 | 142 | shared core moved to `AggregateTracker.cs` (163 lines, one copy) |
  | `ReminderTracker` | 170 | 60 | same core, same file |
  | `TodoListMapper` | 199 | 185 | four-line tail moved to `StoredStamps.cs` (66 lines, one copy) |
  | `ReminderMapper` | 103 | 89 | same helper, same file |
  | `TodoListRepository` | 93 | 93 | untouched |
  | `ReminderRepository` | 114 | 114 | untouched |

  The shared files (`AggregateTracker.cs`, `StoredStamps.cs`) carry more documentation than the code
  they hold strictly needs, because they are the one place a future feature author reads before
  writing a third tracker or a third mapper — the total line count across the persistence layer is
  not smaller, but the logic that used to exist twice now exists once.
- Both concrete trackers keep satisfying `ITodoListTracker`/`IReminderTracker` — and, through them,
  `IAggregateFlusher` and `IDomainEventSource` — entirely through members inherited from
  `AggregateTracker<TAggregate, TRecord>`. `SharedInstanceRegistrationTests` still resolves each
  tracker as one instance under every contract it serves; nothing about that registration changed,
  because `AggregateTracker<TAggregate, TRecord>` is never itself registered — only the two closed,
  concrete generics are.
- `Common/Tracking/` is a new folder under `Common/`, naming itself after the same word the
  per-feature `Tracking/` folders already use — `LayoutConventionTests`' closed-vocabulary check
  only walks `Features/<F>/` folders, so `Common/` is not itself checked, but reusing the feature
  layer's own word rather than inventing a second one for the same concept is the same discipline
  [0025](0025-closed-folder-vocabulary-per-layer.md) asks for, applied by hand where no test yet
  enforces it.
- **Inheritance, not composition, for the tracker.** The shared core and the feature-specific
  `FlushTo` are not two independently varying concerns being assembled — they are the fixed and the
  variable half of one algorithm, which is exactly the shape a template method exists for. The
  alternative, composition, would need the base object's private state (the identity map, the
  restore buffer) exposed through some injected collaborator's surface just so `FlushTo` could
  still reach it — turning "hidden inside the base class" into "handed to a stranger," for no
  actual gain, since there is only one variation point to isolate, not several that might vary on
  separate axes. Composition earns its keep when a type mixes multiple independent behaviours or
  when the varying part must be swapped at runtime; neither is true here. It would also have
  reintroduced the exact duplication being removed: `ITodoListTracker`/`IReminderTracker` are kept as
  two distinct interfaces (each aggregate's registration is asserted separately by
  `SharedInstanceRegistrationTests`, and merging them was never on the table), so a composed-over
  tracker would still need seven hand-written forwarding methods per feature to satisfy its own
  interface — the very lines inheritance lets the compiler supply for free once the generic
  parameters are closed.

## Alternatives rejected

- **A generic `AggregateRepository<TAggregate, TId>`.** Covered above; rejected on the same grounds
  as [0003](0003-aggregate-oriented-repository.md), and disproved by `ReminderRepository` needing two
  methods `TodoListRepository` has no use for.
- **A `Mapper<TAggregate, TRecord>` base class.** Rejected because `ToNewRecord` and `WriteTo` are
  where the two mappers diverge, and a base class would split the one guarantee
  (`ToNewRecord`'s total fidelity, checked by reflection) across two files for the sake of the one
  part — the audit tail — that did turn out to be identical.
- **Folding `FlushTo` into the shared tracker behind a flag or a strategy object.** Rejected because
  it would produce one branchy method where two short, honest ones exist today, and because the
  parameter list such a method would need only grows as a third aggregate arrives with its own
  shape of divergence.
- **Composition over inheritance for the tracker core.** Covered above: it solves a problem this
  code does not have (multiple independently varying concerns) at the cost of exposing state that
  inheritance keeps behind `protected`.

## Revisit when

A third aggregate's tracker turns out to need a `FlushTo` that matches `TodoListTracker`'s or
`ReminderTracker`'s exactly — at that point a shared `FlushTo` helper is justified by two real
examples agreeing, the same bar this record used, rather than by a guess about where a third case
might land. Revisit `StoredStamps` the day an aggregate needs `ToAggregate` to skip a stamp it
carries — its constraint assumes every mapped aggregate is fully auditable and versioned, which is
true of both aggregates that exist today and may not stay true of a third. Revisit the repository
decision under [0003](0003-aggregate-oriented-repository.md)'s own clause: a repository needing more
than about six methods is a sign the aggregate boundary is wrong, not that a base class is due.
