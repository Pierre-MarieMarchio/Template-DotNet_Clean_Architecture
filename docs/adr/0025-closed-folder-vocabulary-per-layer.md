# 0025 — A closed folder vocabulary, one file per public type

Status: Accepted

## Context

A feature-first layout answers "where does X live" by feature name, but says nothing about
what belongs inside a feature folder. Left to grow at each feature's own pace, that second
level drifts in two ways at once. The number of subfolders varies feature to feature, because
nothing stops one feature from earning a folder for a concern that happens once while another
keeps the same handful of files it started with. And a folder named for a capability rather
than a shape — "the customer's access to something," say — ends up holding whatever code
reaches through that capability, so an injected service and a class of extension methods sit
in the same directory because both seemed close enough to belong there when the first file was
added. Neither drift breaks the build. Both make the layout stop answering "where does X live"
for anything beyond the feature name.

A related question sits one level lower: a folder can hold several files, and nothing says a
public type owes the reader a specific one of them, or that the file has to be named for the
type. A misnamed file compiles exactly as well as a well-named one.

## Decision

**Each layer has a closed set of folder names, identical across every feature that uses them,
and a folder exists in a feature only when it holds something.** Nothing outside the set
without a new ADR.

- `Domain/Features/<F>/`: `Entities/`, `ValueObjects/`, `Events/`, `Repositories/`.
- `Application/Features/<F>/`: `UseCases/{Commands,Queries}/<Operation>/`, `Ports/<Port>/`,
  `Consumers/<Event>/`, `Services/`, `Policies/`, `Extensions/`, `Mapping/`, `Dtos/`, `Errors/`.
- `Persistence/Features/<F>/`: `Configurations/`, `Models/`, `Mapping/`, `Queries/`,
  `Repositories/`, `Tracking/`, `Seeding/`, and `Stores/` — the last reserved for the case
  [0024](0024-repository-in-domain-query-ports-in-application.md) describes.
- `Api/Features/<F>/`: `Controllers/`, `Contracts/{Requests,Responses}/`, `Mapping/`.

Within a folder, **one public, non-nested type gets one file, named for the type**, with three
named exceptions:

- An `internal` `IValidateOptions<T>` validator is declared in the same file as the `T` it
  validates, not in a file of its own — the options contract and its startup validation are one
  concept in two halves, and nothing outside the file ever names the validator directly. Around
  twenty option classes follow this; `ShutdownOptions` and the internal
  `ShutdownOptionsValidator` beside it in
  `Src/Presentation/AppTemplate.Api/Common/Lifecycle/ShutdownOptions.cs` is one.
- A **nested type stays in its parent's file.** `LoginOutcome.Authenticated` is declared inside
  `LoginOutcome.cs`, not in a file of its own, because it has no existence apart from the type
  that nests it.
- **Generic arity overloads of the same name count as one type.** `IUseCase`,
  `IUseCase<TResponse>` and `IUseCase<TRequest, TResponse>` are three declarations of one name,
  all in `IUseCase.cs`.

`Mapping/` is the same word regardless of what sits behind it. It names a static class in the
Api and Application layers (`TodoListMapping`, `TodoListProjection`) and an injected service in
Persistence (`ITodoListMapper` / `TodoListMapper`, registered as
`services.TryAddSingleton<ITodoListMapper, TodoListMapper>()` in `PersistenceModule.cs`).
Static versus injected is a fact about the mapping's dependencies, not about what it is for, and
it does not earn its own vocabulary word.

**A type that appears in a port's signature is declared under that port, never under a use
case, no matter how many use cases call the one that happens to be its only consumer today.**
`TodoListPageRequest` has exactly one caller, `GetTodoListsUseCase`, but it is a parameter of
`ITodoListQueries.GetForOwnerAsync`, so it is declared in
`Ports/TodoListQueries/TodoListPageRequest.cs`, not under `UseCases/Queries/GetTodoLists/`.
Filing it by caller count would make `Ports/` depend on `UseCases/` the day a second caller
appears, and the dependency has to run the other way: a port is depended on, a use case does
the depending.

## Consequences

- The vocabulary is small enough to hold in memory, and a reader who knows it can go straight to
  the folder that answers their question in a feature they have never opened.
- One file per type, named for it, means the file tree is a second index of the same
  information the namespace already carries — a search by file name and a search by namespace
  agree.
- The cost is paid in `using` directives. Because a namespace follows a folder and a folder
  follows an operation, a controller that sequences many operations carries one `using` per
  operation. `TodoListsController` calls fifteen use cases and opens with fifteen
  `using AppTemplate.Application.Features.TodoLists.UseCases....` lines, one per operation,
  because there is no folder one level up to import instead. That is the price of a folder that
  answers "which operation" precisely; a coarser folder would shorten the `using` block and blur
  the question the layout exists to answer.
- The rule survives only as long as something checks it.
  `EveryUseCaseFolder_HoldsOneUseCase_AndIsNamedForIt`
  (`Tests/Architecture/AppTemplate.Architecture.Tests/Rules/UseCaseConventionTests.cs`) groups
  every type in an `Application` namespace matching
  `Features.<Vertical>.UseCases.(Commands|Queries).<Operation>` and checks, per group: exactly
  one class ends in `UseCase`; that class is named `<Operation>UseCase`; an interface named
  `I<Operation>UseCase` exists in the same folder; and any record ending in `Command`, `Query`
  or `Request` is named `<Operation><Suffix>`. It only covers the `UseCases` branch — the branch
  with the most folders, and the one where a stray or misnamed file is most expensive, because
  registration resolves a use case by concrete type rather than by scanning.
- Nothing currently checks the exceptions themselves — that an `IValidateOptions<T>` validator
  stays with its `T`, or that a port-signature type stays under `Ports/` — or the rest of the
  vocabulary outside `UseCases/`. Those are held by this record and by review, not by a test.

## Alternatives rejected

- **A folder per concern, opened as soon as one is needed, with no fixed list.** What produces
  the drift this record exists to stop: nothing forces a second feature to reuse the first
  feature's folder name for the same kind of thing, so the same concept accretes two names.
- **One file per folder, no exceptions.** Splits every options class from its validator and
  every closed hierarchy's nested type from its parent, for no reader benefit — both pairs are
  meaningless apart from each other, which is exactly where the exception applies.
- **Filing a port-signature type under whichever use case happens to be its only caller today.**
  Cheapest at the moment the type is added, and it silently reverses the dependency the day a
  second use case calls the same port.

## Revisit when

A feature needs a folder outside this vocabulary for something that is not the repository/store
split [0024](0024-repository-in-domain-query-ports-in-application.md) already covers — that is
the signal to write the next ADR, not to open the folder quietly.
