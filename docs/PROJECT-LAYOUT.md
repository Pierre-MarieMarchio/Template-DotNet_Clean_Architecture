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
  Features/<Feature>/<Concept>/<Nature>/<Subject>/
```

Changing layer does not mean learning a new filing system. The directory under `Src/` names the
**layer**; the project inside keeps its own name and its own root namespace, so moving a project
between layer folders changes no namespace — the disk path and the root namespace are independent.

Within a project the top-level partition is the **business feature**, and only inside a feature is
code grouped by what it does. In `AppTemplate.Domain`, `AppTemplate.Application` and
`AppTemplate.Api` there is deliberately **no `Services/`, `Interfaces/`, `DTOs/`, `Helpers/`,
`Managers/` or `Factories/` folder at the project root**: grouping by technical type at the top
level puts the six files that implement one feature in six different directories, so no change is
ever local and no folder tells you what the application does. A concept or nature folder is
legitimate only *inside* a feature, where it partitions something already cohesive —
[below](#inside-a-feature) is the closed set of them.

`Features/` is the unit of vertical slicing. Three of the thirteen projects under `Src/` have no
`Features/` at all, and each for the same reason: what it holds belongs to no feature.

## The folder map

The map is the template's own full tree, generated with every option on; the projects this solution
actually holds are the ones `README.md` names, and every rule below holds of whatever subset of it a
project was generated with.

```
AppTemplate.Domain.Core         Common/{Abstractions,Events,Exceptions,Primitives}
                                no Features/: a primitive belongs to no feature
<!--#if (tagging)-->
AppTemplate.Domain              Common/{Tagging}
                                Features/<F>/{Model/{Entities,Events,ValueObjects},Repositories}
<!--#else-->
AppTemplate.Domain              Features/<F>/{Model/{Entities,Events,ValueObjects},Repositories}
<!--#endif-->
AppTemplate.Application.Core    Common/{Collections,Concurrency,Events,Idempotency,Localization,
                                        Ownership,Policies,Ports,Results,UseCases,Validation}
                                Features/{Maintenance,DeletedAccounts}/UseCases/Commands/<Operation>
<!--#if (tagging)-->
AppTemplate.Application         Common/{Tagging}
                                Features/<F>/{UseCases/{Commands,Queries}/<Operation>,
<!--#else-->
AppTemplate.Application         Features/<F>/{UseCases/{Commands,Queries}/<Operation>,
<!--#endif-->
                                              Ports/<Port>,
                                              Model/{Dtos,Errors,Mapping,Policies},
                                              Services/Consumers/<Event>,
                                              Configuration/Options,
                                              Helpers}
AppTemplate.Application.Auth    Features/{Accounts,Sessions,TwoFactor,Passkeys,ExternalLogins,
                                          SecurityEvents}/{UseCases/{Commands,Queries}/<Operation>,
                                                           Ports/<Port>,Model/{Errors,Policies}}
                                no Common/: nothing is shared between the six features yet
AppTemplate.Infrastructure.Core Common/{Caching,Contexts,Options,
                                        Saving/{Auditing,DomainEvents,Tracking},Templating,Time}
                                no Features/: a mechanism a module needs and no module owns
AppTemplate.Infrastructure.Persistence
                                Common/{Contexts,Idempotency,Leases}
                                Features/<F>/{Access/{Repositories,Stores,Queries},
                                              Rows/{Models,Configurations,Mapping,Tracking},
                                              Adapters/{Locks,Checkpoints,Observability}}
                                Migrations/   the business half's history
AppTemplate.Infrastructure.Auth Common/{Contexts,Options}
                                Features/{Accounts,Sessions,TwoFactor,Passkeys,ExternalLogins,
                                          SecurityEvents}/{Adapters/{Services,Providers,Directories},
                                                           Access/{Queries,Tables},
                                                           Rows/{Models,Configurations},
                                                           Configuration/{Options,Seeding},
                                                           Resources/Templates}
                                Migrations/   its own history, over the identity schema
AppTemplate.Infrastructure.Email     Common/{Http,Smtp}          Features/<F>/
AppTemplate.Infrastructure.Storage   Common/{Budgets,Factories,Options}
                                     Features/Files/{Access/Stores,
                                                     Adapters/{Inventories,Scanners,Transfers},
                                                     Configuration/Options}
AppTemplate.Infrastructure.InMemory  Common/{Email,Time}         Features/<F>/
AppTemplate.Presentation.Core   Common/{Jobs,Localization,Observability,Outbound,Security}
                                no Features/: what any host needs whatever its transport
AppTemplate.Api                 Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,
                                        Idempotency,Jobs,Localization,Observability,OpenApi,Security}
                                Common/ names no feature: the HTTP mechanisms and the job
                                mechanisms (the role switch, the background scope) every feature shares
                                Features/<F>/{Composition,Configuration/Options,Controllers,
                                              Contracts/{Requests,Responses,Mapping},Jobs}
                                Features/Auth/  one host feature over the six auth features
                                Features/BackgroundJobs/Jobs/  the one composition call, AddBackgroundJobs
Tests/                          a 1:1 mirror of Src/
```

A brace lists every word the project's vocabulary allows; a feature holds only the ones it has
content for. What each word means is [below](#inside-a-feature), once for every project.

<!--#if (localAuth)-->
**Two of those projects own storage, and each owns a migration history.**
`AppTemplate.Infrastructure.Persistence` holds `AppDbContext` over one schema per business feature
(`todo`, `reminders`, `files` and `notes` as the template ships) and `platform`; `AppTemplate.Infrastructure.Auth` holds `AuthDbContext` over `identity`.
[`MIGRATIONS.md`](MIGRATIONS.md) is the commands for both.
<!--#elseif (externalAuth)-->
**One project owns storage, and its migration history.** `AppTemplate.Infrastructure.Persistence`
holds `AppDbContext` over one schema per business feature, `external_identity` (the mapping from the
identity provider's subjects to internal owners) and `platform`. [`MIGRATIONS.md`](MIGRATIONS.md) is
the commands.
<!--#else
**One project owns storage, and its migration history.** `AppTemplate.Infrastructure.Persistence`
holds `AppDbContext` over one schema per business feature and `platform`.
[`MIGRATIONS.md`](MIGRATIONS.md) is the commands.
#endif-->

## Two folders called `Common/`

The difference is the whole point of the split, and there are four kinds in the tree.

**`<Layer>.Core/Common/` is agnostic of the business.** `Result`, `Error`, `PageRequest`,
`SortOrder`, `IUnitOfWork`, `ICurrentUser`, `AggregateRoot<TId>`, `IDomainEvent`, `UserId`. Nothing
in it knows a feature, and nothing in it ever may — that is the whole claim of those projects.

**`<Layer>/Common/` is the business-shared half:** what several features share *as business*, so
business code is not repeated across features — a value object three features spend, a DTO two
features answer with, a policy that spans them.

**The API's `Common/` is a third kind:** the HTTP mechanisms every feature of that host shares —
the controller base, problem details, preconditions, idempotency, the security headers and the
request pipeline — together with what the host cannot delegate, which is whatever names a concrete
infrastructure module or decides a deployment policy. It stays in the host rather than in a `.Core`
project because it is HTTP-specific and has no second consumer; it names no feature, which an
architecture test holds now that the compiler no longer does.

**The sorting test is one question: does it know a feature?** If it names a feature — or would have
to, the moment a second feature used it — it belongs in the business project's `Common/`. If it
does not and never will, it belongs one project inwards.

<!--#if (todoLists && files)-->
`Common/Tagging/` is the worked example, in both business layers. A to-do item and a stored file
are both tagged, and by the same rule — a tag is held once however many times it is sent, a cap
bounds how many one thing carries, and a replacement is total rather than a merge — so `TagSet`
states it once and both aggregates own one. **The test that put it there rather than one project
inwards is mechanical: `TagSet` names `Tag`, and a `.Core` project may name no business type.** Its
generic shape, a bounded de-duplicating collection, would have been agnostic; what it actually is
is not. The application half holds `TagValidation`, so a caller gets one 400 naming every tag it
got wrong rather than the first, and `UsedTagsCache`, which is where the key and the lifetime of
the used-tag read are decided once instead of once per feature.
<!--#elseif (todoLists)-->
`Common/Tagging/` is the worked example, in both business layers. A to-do item is
tagged by a rule every tagged feature will share — a tag is held once however many times it is sent, a cap
bounds how many one thing carries, and a replacement is total rather than a merge — so `TagSet`
states it once and the to-do list aggregate owns one. **The test that put it there rather than one project
inwards is mechanical: `TagSet` names `Tag`, and a `.Core` project may name no business type.** Its
generic shape, a bounded de-duplicating collection, would have been agnostic; what it actually is
is not. The application half holds `TagValidation`, so a caller gets one 400 naming every tag it
got wrong rather than the first, and `UsedTagsCache`, which is where the key and the lifetime of
the used-tag read are decided once instead of once per feature.
<!--#elseif (files)-->
`Common/Tagging/` is the worked example, in both business layers. A stored file is
tagged by a rule every tagged feature will share — a tag is held once however many times it is sent, a cap
bounds how many one thing carries, and a replacement is total rather than a merge — so `TagSet`
states it once and the stored file aggregate owns one. **The test that put it there rather than one project
inwards is mechanical: `TagSet` names `Tag`, and a `.Core` project may name no business type.** Its
generic shape, a bounded de-duplicating collection, would have been agnostic; what it actually is
is not. The application half holds `TagValidation`, so a caller gets one 400 naming every tag it
got wrong rather than the first, and `UsedTagsCache`, which is where the key and the lifetime of
the used-tag read are decided once instead of once per feature.
<!--#else-->
This project has no business `Common/` yet: no two of its features share a business type. The
first rule that two features share and that names one of their types — a tag set held by two
aggregates, say — is what earns the domain and application projects their first `Common/`
folder.
<!--#endif-->

What is *not* there is as informative. The ownership check every feature performs and the paginated
read every feature exposes are both duplicated and both agnostic — they name no feature and never
would — so they live one project inwards. A folder in the business `Common/` is earned by naming a
business type, not by being repeated.

**An infrastructure module's `Common/` is the fourth:** what serves the module itself and names no
feature — its context, the settings every one of its features shares. Everything that implements a
port of a vertical, or names a vertical's types, files under that vertical, in a module serving one
feature no less than in one serving several.
<!--#if (localAuth)-->
That is why `AppTemplate.Infrastructure.Auth`'s `Common/` is `Contexts/` and `Options/` and nothing
else, and why the account directory every auth feature reads sits under
`Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/Accounts/Adapters/Directories/`.
<!--#endif-->
`ModuleDependencyTests` holds it: nothing under a module's `Common/` may depend on a feature —
`AppDbContext`, [below](#the-rules-the-tests-hold), is the one exception the rule is written
around — and its list of further exceptions is empty and asserted empty, so the first one is argued
for in the pull request that adds it.

## Inside a feature

### Four levels, one question each

```
Level 1 — the partition   Common/ or Features/<F>/              does it know a feature?
Level 2 — the concept     one of the closed set below            what role does it play?
Level 3 — the nature      the plural of the type's suffix        what is it?
Level 4 — the subject     Ports/<Port>/ · UseCases/Commands/<Operation>/ · Consumers/<Event>/
```

A concept folder exists only when it has content: a feature with no read-side projection has no
`Access/Queries/`, one with no domain-event consumer has no `Services/Consumers/`. A nature folder
is always present under its concept — a folder even for a single file. `Ports/` and `UseCases/`
always open one folder per port and per operation, because that folder is the signature's
namespace; any other nature opens subject folders only once it holds several subjects.
<!--#if (notes && todoLists)-->
`Notes`' one consumer sits flat in `Services/Consumers/`, where `TodoLists`' two each have a
folder named for the event they answer.
<!--#endif-->

### The concepts

Every feature folder is readable in one sentence per concept, and each concept answers one
mechanical question:

| Concept | The question | What it holds (its natures) | Layer |
|---|---|---|---|
| `UseCases/` | is it an operation? | `Commands/<Operation>/` and `Queries/<Operation>/`, each holding the command or query, the named interface, the use case and its validator | Application |
| `Ports/` | is it a contract something else implements? | `<Port>/`, holding the interface and the messages that cross it | Application |
| `Model/` | does the feature *say* it? | `Entities/`, `ValueObjects/`, `Events/` (Domain); `Dtos/` with the validator of an input Dto, `Mapping/` (aggregate to Dto), `Policies/` (its declared rules), `Errors/` (its failure catalogue) (Application) | Domain, Application |
| `Services/` | is it an internal collaborator with dependencies? | the gate every command loads its aggregate through, or a simple feature's one service; `Consumers/` | Application |
| `Repositories/` | is it the aggregate's access contract? | the `I…Repository`, in domain types only | Domain |
| `Adapters/` | does it implement a port or a framework seam, outside storage? | `Services/` (a `…Service`, `…Factory`, `…Issuer`, `…Log` or `…Verifier`), `Providers/`, `Directories/`, `Scanners/`, `Inventories/`, `Transfers/`, `Locks/`, `Checkpoints/`, `Observability/` | Infrastructure |
| `Rows/` | is it the *shape* of what is stored? | `Models/` (the `…Record` rows), `Configurations/`, `Mapping/` (aggregate to row), `Tracking/` | Infrastructure |
| `Access/` | is it one of the four ways this template reaches storage? | `Repositories/`, `Stores/`, `Queries/`, `Tables/` | Infrastructure |
| `Configuration/` | is it a setting? | `Options/`, `Seeding/` | Application, Infrastructure, Api |
| `Resources/` | is it an embedded file? | `Templates/` | Infrastructure |
| `Contracts/` | is it a wire type? | `Requests/`, `Responses/`, and `Mapping/` that projects onto them — never an application type reused | Api |
| `Controllers/` | is it an HTTP entry point? | the controllers, flat | Api |
| `Jobs/` | is it a loop on a timer? | the `…BackgroundService`, its options, its instruments, the extension that registers them | Api |
| `Composition/` | is it what the host supplies to the feature? | the feature's `AddX` entry point and the adapters only the host can provide — a directory joining two features that may not name each other, a provider reading the request, the caller's session | Api |
| `Helpers/` | is it a pure static function with no more precise nature? | the `…Helpers` and `…Extensions` classes, flat | Application, Infrastructure, Api |

Fifteen concepts in all, and two to five present in any one feature. A word with several meanings
across scopes keeps one definition per scope in this table, which is how `Services/` (an
application collaborator, and under `Adapters/` an infrastructure port adapter) and `Mapping/`
(`Model/Mapping/`, `Rows/Mapping/`, `Contracts/Mapping/`) stay unambiguous.

**`Model/` is not `Rows/Models/`.** `Model/` is a concept of the inner layers: what the feature
says — its aggregate, its value objects, its events, the Dtos it answers with. `Rows/Models/` is a
nature of the persistence rows: the `…Record` a table is mapped to, which is never the aggregate.
The two never meet in one project, and the singular and the plural are kept apart on purpose.

### The suffix gives the folder

A type's name gives its folder in at most two hops — its suffix names the nature, and the nature
belongs to one concept per layer:

| Suffix of the type | Folder |
|---|---|
| `…UseCase`, `…Command`, `…Query`, and an operation's `…Validator` | `UseCases/{Commands,Queries}/<Operation>/` |
| a port's interface, and the messages that cross it | `Ports/<Port>/` |
| `…Repository` — the contract / the adapter | `Repositories/` (Domain) / `Access/Repositories/` |
| an entity, a value object, an `…Event` | `Model/Entities/`, `Model/ValueObjects/`, `Model/Events/` |
| `…Dto`, and the `…Validator` of an input Dto | `Model/Dtos/` |
| `…Mapping` (aggregate to Dto) | `Model/Mapping/` |
| `…Policy` | `Model/Policies/` |
| `…Errors` | `Model/Errors/` |
| `…Service` (application) | `Services/` |
| `…Consumer` | `Services/Consumers/` |
| `…Helpers`, `…Extensions` | `Helpers/` |
| `…Store`, `…Queries` (with its `…SortMap`), `…Table` (with its `…Grant`) | `Access/Stores/`, `Access/Queries/`, `Access/Tables/` |
| `…Record`, `…Configuration`, `…Mapper`, `…Tracker` | `Rows/Models/`, `Rows/Configurations/`, `Rows/Mapping/`, `Rows/Tracking/` |
| `…Service`, `…Factory`, `…Issuer`, `…Log`, `…Verifier` (infrastructure) | `Adapters/Services/` |
| `…Provider`, `…Directory` | `Adapters/Providers/`, `Adapters/Directories/` — in the Api, `Composition/` |
| `…Scanner`, `…Inventory`, `…Transfer`, `…Lock`, `…Checkpoint` | the matching folder under `Adapters/` |
| `…Diagnostics`, `…Instruments` | `Adapters/Observability/` — in the Api, `Jobs/` |
| `…Options` | `Configuration/Options/` — a job's own, `Jobs/` |
| `…Seeder` | `Configuration/Seeding/` |
| `…Request`, `…Response`, `…Mapping` (wire) | `Contracts/Requests/`, `Contracts/Responses/`, `Contracts/Mapping/` |
| `…Controller` | `Controllers/` |
| `…BackgroundService`, `…Task`, `…Telemetry` | `Jobs/` |
| `…Composition`, `…Session` | `Composition/` |

`FeatureFolderVocabularyTests` holds that table from the file side — a file whose suffix is not its
folder's fails, a folder no entry describes fails, and every exemption is named with its reason and
must still name a file — and `LayoutConventionTests` from the folder side, per project.

### Co-localisation

```
What serves ONE operation      → in the operation's folder         UseCases/{Commands,Queries}/<Operation>/
What serves ONE port           → with the port                    Ports/<Port>/
What serves SEVERAL operations → promoted to the concept folders  Model/, Services/, …
What has a more precise nature → that nature's folder             a mapper is Model/Mapping/, not Helpers/
```

**A command or query record lives in the same folder as the one use case that accepts it**,
alongside that use case's named interface, its class and its validator, because together they are
that operation's signature. A response type only that one operation returns stays there too; a read
model more than one operation shares is promoted to `Model/Dtos/`; and a type that is a *port's*
own parameter — not one use case's — lives beside that port in `Ports/<Port>/` instead, however
many use cases call it. Otherwise `Ports/` would depend on `UseCases/`.

**`Helpers/` is the last resort, never the first.** It holds a pure static function — formatting,
computation, parsing, an extension method — that has no more precise nature; a file that has one
goes to that nature's folder. The bounds, and why `Helpers` is the one such word allowed, are
[`CONVENTIONS.md`](CONVENTIONS.md#helpers-and-the-banned-words)'s, and so is why a `Common/` has
none.

<!--#if (todoLists)-->
### The worked example

`AppTemplate.Application/Features/TodoLists/`:

```
UseCases/Commands/<Operation>/  one folder per operation, each holding its command, its named
                                interface, the use case and its FluentValidation validator
UseCases/Queries/<Operation>/   the same shape on the read side
Ports/TodoListQueries/          ITodoListQueries and TodoListFilter — a port's interface together
                                with the messages that cross it
Model/Dtos/                     read models more than one operation returns
Model/Errors/                   TodoListErrors — the feature's failure vocabulary
Model/Mapping/                  TodoListDtoMapping — the aggregate a write just staged, read back
                                as a Dto
Model/Policies/                 TodoListCollectionPolicy — the sortable whitelist
Services/                       ITodoListService — the one gate every command loads its aggregate
                                through
Services/Consumers/<Event>/     one folder per domain event the feature answers —
                                TodoItemCompleted, AccountDeleted
Helpers/                        TodoListItemExtensions — a known-item id turned into the same 404
                                everywhere
```
<!--#else
### The rich shape

A rich feature's application folder, `AppTemplate.Application/Features/<Feature>/`:

```
UseCases/Commands/<Operation>/  one folder per operation, each holding its command, its named
                                interface, the use case and its FluentValidation validator
UseCases/Queries/<Operation>/   the same shape on the read side
Ports/<Aggregate>Queries/       I<Aggregate>Queries and <Aggregate>Filter — a port's interface
                                together with the messages that cross it
Model/Dtos/                     read models more than one operation returns
Model/Errors/                   <Aggregate>Errors — the feature's failure vocabulary
Model/Mapping/                  <Aggregate>DtoMapping — the aggregate a write just staged, read
                                back as a Dto
Model/Policies/                 <Aggregate>CollectionPolicy — the sortable whitelist
Services/                       I<Aggregate>Service — the one gate every command loads its
                                aggregate through
Services/Consumers/<Event>/     one folder per domain event the feature answers
Helpers/                        <Aggregate>…Extensions — a pure function several operations need
```
#endif-->

### The simple shape

The simple shape, a feature with no domain model, has no operation folders: its one service holds
one method per operation, so the validator of an input Dto files beside that Dto in `Model/Dtos/`.
Its persistence half uses the one word the rich shape does not: `Access/Stores/` is the adapter of
an application `…Store` port, the way `Access/Repositories/` is the adapter of a domain
`…Repository`.

<!--#if (notes)-->
```
Application/Features/Notes/
  Model/Dtos/             NoteDto, NoteSummaryDto, the service's input NoteContentDto and
                          NoteListQueryDto, and NoteContentDtoValidator beside the Dto it validates
  Model/Errors/           NoteErrors
  Model/Policies/         NoteInputPolicy (the length limits), NoteCollectionPolicy (the sortable
                          whitelist)
  Ports/NoteQueries/      INoteQueries and NoteFilter — the reads
  Ports/NoteStore/        INoteStore and NoteChangeStatus — the writes, which only stage
  Services/               INoteService and NoteService — one method per operation
  Services/Consumers/     DeleteNotesOnAccountDeletedConsumer, flat: the one consumer there is
Infrastructure.Persistence/Features/Notes/
  Rows/Models/            NoteRecord — the row, and the only model there is
  Rows/Configurations/    NoteRecordConfiguration
  Access/Queries/         NoteQueries and NoteSortMap
  Access/Stores/          NoteStore
```
<!--#endif-->

<!--#if (todoLists)-->
The other two examples are the same shape as `TodoLists` with pieces added or missing, and what each one changes
is the subject of [`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md): `Reminders` is flat, with no child
entities and so no tag folder; `Files` adds four ports over its bytes — a store, a direct
transfer, an inventory and a scanner — because the bytes live in somebody else's store.
<!--#endif-->

<!--#if (localAuth)-->
### The authentication features

`AppTemplate.Application.Auth/Features/` holds six features with no aggregate at all —
`Accounts`, `Sessions`, `TwoFactor`, `Passkeys`, `ExternalLogins`, `SecurityEvents` — each
`UseCases/`, `Ports/` and `Model/`. Fourteen ports stand in for one `IAuthService`, one per intent:
accounts, mailed tokens, re-authentication, administration and account mails under `Accounts`;
session tokens, sessions and the current session under `Sessions`; two-factor; passkeys; external
identity and external logins under `ExternalLogins`; the security event log and its queries under
`SecurityEvents`. An account here is what its ports say about it. Five of the six have an error
catalogue; the failures several features answer with — re-authentication required, a missing or
incorrect current password — are declared once, in `AccountsErrors`, and the others name them
there. `AppTemplate.Infrastructure.Auth/Features/` repeats the six names, each feature holding the
adapters of its own ports.

### The host mapping

One Api feature maps to one application feature everywhere except the authentication surface:
`Src/Presentation/AppTemplate.Api/Features/Auth/` is one host feature whose controllers answer use
cases of all six auth features — one `/auth` route tree, one composition entry
(`AddAuthFeature`), one folder for a host without local accounts to delete. A controller crosses
the six — `SessionsController` signs in through `Sessions`, completes a two-factor challenge
through `TwoFactor` and accepts an external provider's token through `ExternalLogins` — and those
are one route tree's actions, not three surfaces'.
<!--#endif-->

### Every module keeps the shape

<!--#if (reminders)-->
**Every infrastructure module is partitioned `Common/` plus `Features/<Feature>/`**, whether or not
it serves more than one feature, so a reader who has learned one module does not learn a second
filing system for the next. `Email` and `InMemory` carry both kinds of adapter — a transverse
`IEmailSender` and, in the template as shipped, a feature-scoped `IReminderNotifier` — so their tree
says which of their files leave with a feature.
<!--#else
**Every infrastructure module is partitioned `Common/` plus `Features/<Feature>/`**, whether or not
it serves more than one feature, so a reader who has learned one module does not learn a second
filing system for the next.
#endif-->
<!--#if (files)-->
`Storage` serves one feature, `Files`, and keeps the shape anyway.
<!--#endif-->
Local logic loses to uniformity here on purpose.

## The rules the tests hold

- **A folder even for a single file.** No `.cs` at a project root except the `.csproj`, the DI
  module class, and `Program.cs`.
- **No `Services/`, `Interfaces/`, `DTOs/`, `Helpers/`, `Managers/` at a project root.** Sorting by
  technical type is banned there, and allowed only inside a feature.
- **Namespaces follow folders.** No exceptions.
- **Never name a folder after the type it contains.** A namespace and a type sharing a name make
  name resolution ambiguous for consumers (CS0118), because lookup walks the enclosing namespaces.
  A port's folder therefore keeps the capability name — `Ports/<Capability>/I<Capability>Service.cs`,
  never `Ports/I<Capability>Service/`.
- **The vocabulary under `Features/<F>/` is closed, per project, and two-tier**: the concepts a
  project's features may hold at level 2, and under each concept the natures it may hold at
  level 3. Both are checked in both directions — a folder outside the list fails, and, over the
  template's full tree, so does a word no feature is filed under any more. The first level of `Common/` is closed too; how a
  `Common/` folder partitions itself below that is its own business, which is why `Saving/` may
  hold `Auditing/`, `DomainEvents/` and `Tracking/`.
- **The files are held to it too, not only the folder names.** A file under a nature folder is
  named for that nature, [as tabled above](#the-suffix-gives-the-folder). Three kinds of file are
  exempt, each named with its reason in the test: a type in another thing's signature, a name a
  framework imposes, and the state an adapter holds.
- **Every type under a `Helpers/` folder is a static class**, which also bars it from implementing
  an interface, so it can be neither injected nor a contract.
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
under a list of words.
<!--#if (localAuth)-->
`AppTemplate.Application.Auth` is the one null `Common/`,
<!--#endif-->
and the empty
`Features/` word lists belong to the two smallest infrastructure modules, so the first subfolder anyone
adds there has to be argued for in the pull request that adds it.

## The word "Module"

Reserved for exactly one thing: the dependency-injection registration classes —
`ApplicationCoreModule`, `ApplicationModule`, `ApplicationAuthModule`, `InfrastructureCoreModule`,
`PersistenceModule`, `AuthModule`, `EmailModule`, `StorageModule`, `InMemoryModule`,
`PresentationCoreModule` and `HttpPipelineModule`. That is a composition concept, not a business
partition, and composition is [`ARCHITECTURE.md`](ARCHITECTURE.md#infrastructure-is-split-per-capability-with-no-per-technology-sub-split)'s
subject: one `AddX()` per feature, opt-in by the host, and deliberately no call that adds a layer
whole — which is what makes a feature removable by deleting its folder and its one line.

<!--#if (reminders)-->
**`AddReminders()` is not independent of `AddTodoLists()`.** A host that composes the one without
the other resolves nothing: scheduling a reminder reads the to-do list's port, which
[`ARCHITECTURE.md`](ARCHITECTURE.md#infrastructure-is-split-per-capability-with-no-per-technology-sub-split)
names among the couplings a removal meets, and
[`REMOVING-THE-EXAMPLE-FEATURES.md`](REMOVING-THE-EXAMPLE-FEATURES.md) is the procedure.
<!--#endif-->
