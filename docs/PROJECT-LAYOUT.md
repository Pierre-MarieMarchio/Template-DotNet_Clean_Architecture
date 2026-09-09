# Project layout

Where everything lives, and the rules that put it there. Why the layers are drawn this way is
[`ARCHITECTURE.md`](ARCHITECTURE.md); this page is the map, and every rule below is enforced by a
test in `Tests/Architecture/AppTemplate.Architecture.Tests` rather than by review.

- [One shape, every project](#one-shape-every-project)
- [The folder map](#the-folder-map)
- [Two folders called `Common/`](#two-folders-called-common)
- [Inside a feature](#inside-a-feature)
- [The rules the tests hold](#the-rules-the-tests-hold)
- [Adding a project](#adding-a-project)
- [The word "Module"](#the-word-module)

## One shape, every project

```
Src/<Layer>/<Project>/
  Common/<Responsibility>/
  Features/<Feature>/<Responsibility>/
```

Changing layer does not mean learning a new filing system. The directory under `Src/` names the
**layer**; the project inside keeps its own name and its own root namespace, so moving a project
between layer folders changes no namespace — the disk path and the root namespace are independent.

Within a project the top-level partition is the **business feature**, and only inside a feature is
code grouped by what it does. In `AppTemplate.Domain`, `AppTemplate.Application` and
`AppTemplate.Api` there is deliberately **no `Services/`, `Interfaces/`, `DTOs/`, `Helpers/`,
`Managers/` or `Factories/` folder at the project root**: grouping by technical type at the top
level puts the six files that implement one feature in six different directories, so no change is
ever local and no folder tells you what the application does. A responsibility folder is legitimate
only *inside* a feature, where it partitions something already cohesive.

`Features/` is the unit of vertical slicing. Four of the fifteen projects under `Src/` have no
`Features/` at all, and each for the same reason: what it holds belongs to no feature.

## The folder map

```
AppTemplate.Domain.Core         Common/{Abstractions,Events,Exceptions,Primitives}
                                no Features/: a primitive belongs to no feature
AppTemplate.Domain              Common/{Tagging}
                                Features/<F>/{Entities,Events,ValueObjects,Repositories}
AppTemplate.Application.Core    Common/{Collections,Concurrency,Events,Idempotency,Localization,
                                        Ownership,Policies,Ports,Results,UseCases,Validation}
                                Features/Maintenance/UseCases/Commands/<Operation>
AppTemplate.Application         Common/{Tagging}
                                Features/<F>/{UseCases/{Commands,Queries}/<Operation>,
                                              Ports/<Port>,Consumers,Services,Policies,
                                              Extensions,Mapping,Dtos,Errors}
AppTemplate.Application.Auth    Features/Auth/{Errors,Policies,Ports/<Port>,
                                               UseCases/{Commands,Queries}/<Operation>}
                                no Common/: one feature, so nothing is shared between features
AppTemplate.Infrastructure.Core Common/{Caching,Contexts,Options,
                                        Saving/{Auditing,DomainEvents,Tracking},Templating,Time}
                                no Features/: a mechanism a module needs and no module owns
AppTemplate.Infrastructure.Persistence
                                Common/{Contexts,Idempotency,Leases}
                                Features/<F>/{Models,Configurations,Mapping,Tracking,
                                              Repositories,Queries,Observability}
                                Migrations/   the business half's history
AppTemplate.Infrastructure.Auth Common/{Contexts,Directories,Options}
                                Features/Auth/{Configurations,Directories,Factories,Issuers,Logs,
                                               Models,Options,Providers,Seeding,Services,Tables,
                                               Templates,Verifiers}
                                Migrations/   its own history, over the identity schema
AppTemplate.Infrastructure.Email     Common/{Http,Smtp}          Features/<F>/
AppTemplate.Infrastructure.Storage   Common/{Budgets,Factories,Options}
                                     Features/Files/{Inspectors,Inventories,Options,Scanners,Stores}
AppTemplate.Infrastructure.InMemory  Common/{Email,Time}         Features/<F>/
AppTemplate.Presentation.Core   Common/{Jobs,Localization,Observability,Outbound,Security}
                                no Features/: what any host needs whatever its transport
AppTemplate.Api.Core            Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,
                                        Idempotency,Localization,Observability,OpenApi,Security}
                                no Features/: the half of an HTTP host that knows no feature
AppTemplate.Api                 Common/{Hosting,Observability,Security}
                                Features/<F>/{Controllers,Contracts/{Requests,Responses},Mapping}
AppTemplate.Worker              Common/{Observability,Security}
                                Features/<F>/   one BackgroundService, its options, its metrics
Tests/                          a 1:1 mirror of Src/
```

**Two of those projects own storage, and each owns a migration history.**
`AppTemplate.Infrastructure.Persistence` holds `AppDbContext` over the `todo`, `reminders`, `files`
and `platform` schemas; `AppTemplate.Infrastructure.Auth` holds `AuthDbContext` over `identity`.
[`MIGRATIONS.md`](MIGRATIONS.md) is the commands for both.

## Two folders called `Common/`

The difference is the whole point of the split, and there are three kinds in the tree.

**`<Layer>.Core/Common/` is agnostic of the business.** `Result`, `Error`, `PageRequest`,
`SortOrder`, `IUnitOfWork`, `ICurrentUser`, `AggregateRoot<TId>`, `IDomainEvent`, `UserId`. Nothing
in it knows a feature, and nothing in it ever may — that is the whole claim of those projects.

**`<Layer>/Common/` is the business-shared half:** what several features share *as business*, so
business code is not repeated across features — a value object three features spend, a DTO two
features answer with, a policy that spans them.

**A host's `Common/` is a third kind:** what that host cannot delegate — what names a concrete
infrastructure module, or decides a deployment policy.

**The sorting test is one question: does it know a feature?** If it names a feature — or would have
to, the moment a second feature used it — it belongs in the business project's `Common/`. If it
does not and never will, it belongs one project inwards.

`Common/Tagging/` is the worked example, in both business layers. A to-do item and a stored file
are both tagged, and by the same rule — a tag is held once however many times it is sent, a cap
bounds how many one thing carries, and a replacement is total rather than a merge — so `TagSet`
states it once and both aggregates own one. **The test that put it there rather than one project
inwards is mechanical: `TagSet` names `Tag`, and a `.Core` project may name no business type.** Its
generic shape, a bounded de-duplicating collection, would have been agnostic; what it actually is
is not.

What is *not* there is as informative. The ownership check every feature performs and the paginated
read every feature exposes are both duplicated and both agnostic — they name no feature and never
would — so they live one project inwards. A folder in the business `Common/` is earned by naming a
business type, not by being repeated.

## Inside a feature

**A folder under `Features/<F>/` is the plural of the nature word its files carry.** A
`…Repository` is in `Repositories/`, a `…Mapper` in `Mapping/`, a `…Tracker` in `Tracking/`, a
`…Service` in `Services/` — and `Services/` holds nothing that is not one. That is what makes the
tree navigable in both directions: a type name tells you its folder, and a folder tells you what
nature of thing is in it.

A folder is only present when it has content: a feature with no read-side projection has no
`Queries/`, one with no domain-event consumer has no `Consumers/`.

`AppTemplate.Application/Features/TodoLists/` is the worked example:

```
Errors/                   TodoListErrors.cs — the feature's failure vocabulary
Policies/                 TodoListCollectionPolicy — the sortable whitelist
Ports/TodoListQueries/    ITodoListQueries and TodoListFilter — a port's interface together with
                          the messages that cross it
Services/                 ITodoListService — the one gate every command loads its aggregate through
Extensions/               TodoListItemExtensions — a known-item id turned into the same 404 everywhere
Mapping/                  TodoListDtoMapping — the aggregate a write just staged, read back as a DTO
Consumers/TodoItemCompleted/    a worked example of a domain-event consumer
UseCases/Commands/<Operation>/  one folder per operation, each holding its command, its named
                          interface, the use case and its FluentValidation validator
UseCases/Queries/<Operation>/   the same shape on the read side
Dtos/                     read models more than one operation returns
```

**A command or query record lives in the same folder as the one use case that accepts it**,
alongside that use case's named interface, its class and its validator, because together they are
that operation's signature. A response type only that one operation returns stays there too; a read
model more than one operation shares is promoted to `Dtos/`; and a type that is a *port's* own
parameter — not one use case's — lives beside that port in `Ports/<Port>/` instead, however many
use cases call it. Otherwise `Ports/` would depend on `UseCases/`.

The other two examples are the same shape with pieces added or missing, and what each one changes
is the subject of [`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md): `Reminders` is flat, with no child
entities and so no tag folder; `Files` has four ports where `TodoLists` has one, because the bytes
live in somebody else's store.

`AppTemplate.Application.Auth/Features/Auth/` is the one vertical with no aggregate at all. Twenty
ports stand in for one `IAuthService`, one per capability — accounts, profiles, deletion, lockouts,
roles, token issuing, refresh grants, refresh maintenance, the three token-and-mail pairs, external
identity and logins, two-factor enrolment, challenge and administration, and the security event
log. An account here is what its ports say about it.

**Every infrastructure module is partitioned `Common/` plus `Features/<Feature>/`**, whether or not
it serves more than one feature, so a reader who has learned one module does not learn a second
filing system for the next. `Email` and `InMemory` carry both kinds of adapter — a transverse
`IEmailSender` and a feature-scoped `IReminderNotifier` — so their tree says which of their files
leave with the reminders example. `Auth` serves one feature and `Storage` serves one feature, and
both keep the shape anyway. Local logic loses to uniformity here on purpose.

## The rules the tests hold

- **A folder even for a single file.** No `.cs` at a project root except the `.csproj`, the DI
  module class, and `Program.cs`.
- **No `Services/`, `Interfaces/`, `DTOs/`, `Helpers/`, `Managers/` at a project root.** Sorting by
  technical type is banned there, and allowed only inside a feature.
- **Namespaces follow folders.** No exceptions.
- **Never name a folder after the type it contains.** A namespace and a type sharing a name make
  name resolution ambiguous for consumers (CS0118), because lookup walks the enclosing namespaces.
  A port's folder therefore keeps the capability name — `Ports/UserAccounts/IUserAccountsService.cs`.
- **The vocabulary under `Features/<F>/` is closed, per project**, and so is the first level of
  `Common/`. How a `Common/` folder partitions itself below that first level is its own business,
  which is why `Saving/` may hold `Auditing/`, `DomainEvents/` and `Tracking/`.
- **The files are held to it too, not only the folder names.** A file under
  `Features/<F>/<Word>/` is named for that word. Three kinds of file are exempt, each named with
  its reason in the test: a type in another thing's signature, a name a framework imposes
  (`AppUser`, `AppRole`), and the state an adapter holds.
- **`Tests/` mirrors `Src/`** one directory for one directory.
- **Nothing under `AppTemplate.Infrastructure.Persistence`'s `Common/` may depend on a feature's
  domain or persistence types.** `AppDbContext` is the one documented exception: it applies every
  feature's configuration, which is what makes it the model's composition root. The rule checks
  type dependencies rather than identifiers, so a file merely *named* after a feature would not
  trip it.

## Adding a project

Two of those rules iterate a list of projects, and a rule that iterates a list it does not own
passes by saying nothing about what the list omits. So a third rule reads every project under
`Src/` off the disk and requires an entry in both vocabularies: **a new project of any layer fails
the build until its layout is described in `Tests/Architecture/AppTemplate.Architecture.Tests` and
in the tree above.**

**An entry may be null, meaning "this project has no such folder today", and that claim is checked
in both directions.** Null is not an empty word list: empty says the folder exists and its features
hold their files side by side, null says the folder is not there at all. So a `Common/` or
`Features/` appearing where the entry says none fails the build, and so does one vanishing from
under a list of words. `AppTemplate.Application.Auth` is the one null `Common/`; `AppTemplate.Worker`
is the one empty `Features/` word list, so the first subfolder anyone adds there has to be argued
for in the pull request that adds it.

## The word "Module"

Reserved for exactly one thing: the dependency-injection registration classes —
`ApplicationCoreModule`, `ApplicationModule`, `ApplicationAuthModule`, `InfrastructureCoreModule`,
`PersistenceModule`, `AuthModule`, `EmailModule`, `StorageModule`, `InMemoryModule`,
`PresentationCoreModule` and `ApiCoreModule`. That is a composition concept, not a business
partition.

**Composition is opt-in per feature, and there is deliberately no call that adds all of them.**
`ApplicationModule` exposes `AddTodoLists()`, `AddReminders()` and `AddFiles()`;
`ApplicationAuthModule` exposes `AddAuthApplication()`; `ApplicationCoreModule` exposes
`AddPurgeExpiredIdempotencyKeys()` and the registration helpers the layer above composes with, but
no umbrella `AddApplicationCore()` — ports, results and markers are not things a container
registers, and a call that registered nothing would read like the seam that makes the project work.

That is what makes a feature removable: deleting its folder and its one line is the whole
operation, and nothing else claims to have registered it. One entry point scanning a layer's
assembly would instead put every port every feature declares into every host's graph, which under
`ValidateOnBuild` makes each of them mandatory everywhere.

**`AddReminders()` is not independent of `AddTodoLists()`.** Scheduling a reminder reaches into the
to-do list's read port, which is the only ownership check made before a reminder is created, so a
host that composes the one without the other resolves nothing. [`ARCHITECTURE.md`](ARCHITECTURE.md)
names the other couplings that a removal meets, and
[`REMOVING-THE-EXAMPLE-FEATURES.md`](REMOVING-THE-EXAMPLE-FEATURES.md) is the procedure.
