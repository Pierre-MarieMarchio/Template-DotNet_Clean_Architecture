# Project layout

Where a file goes and what it is called. Every rule on this page is held by a test in
`Tests/Architecture/AppTemplate.Architecture.Tests`, so a misplaced file fails the build. Why the
layers are drawn this way is [the architecture](../architecture/README.md).

## The folder map

Every project has the same shape: `Common/` for what knows no feature, `Features/<Feature>/` for the
rest, and a root that holds only the `.csproj`, one composition file (`Program.cs` or the project's
`…Module.cs`) and the folders `Common/`, `Features/`, `Migrations/` and `Properties/`.
<!--#if (presentationBlazorServer)-->
The presentation project's root also holds `Components/` and `wwwroot/`, which its framework's
template writes and which are its own from then on.
<!--#endif-->
<!--#if (presentationAvalonia)-->
The presentation project's root also holds `Assets/`, `ViewModels/`, `Views/`, `App.axaml.cs` and
the view locator, which its framework's template writes and which are its own from then on; a file
named `<Type>.axaml.cs` declares `<Type>`.
<!--#endif-->
<!--#if (dotnetClient)-->
The .NET client's root also holds `Generated/`, which `dotnet run Tools/Tasks.cs api-client`
writes and nobody edits ([ADR 0023](../decisions/0023-a-dotnet-client-generated-by-kiota.md)).
<!--#endif-->

A brace lists every word a project allows at that level; a feature holds only the words it has
content for.

```
Src/Domain/
  AppTemplate.Domain.Core               Common/{Abstractions,Events,Exceptions,Primitives}
                                        no Features/
<!--#if (tagging)-->
  AppTemplate.Domain                    Common/{Tagging}
<!--#endif-->
<!--#if (!tagging)-->
  AppTemplate.Domain                    no Common/
<!--#endif-->
                                        Features/<F>/{Model/{Entities,Events,ValueObjects},Repositories}
Src/Application/
  AppTemplate.Application.Core          Common/{Collections,Concurrency,Events,Idempotency,Localization,
                                                Ownership,Policies,Ports,Results,UseCases,Validation}
                                        Features/<F>/UseCases/Commands/<Operation>
<!--#if (tagging)-->
  AppTemplate.Application               Common/{Tagging}
<!--#endif-->
<!--#if (!tagging)-->
  AppTemplate.Application               no Common/
<!--#endif-->
                                        Features/<F>/{UseCases/{Commands,Queries}/<Operation>,
                                                      Ports/<Port>,
                                                      Model/{Dtos,Errors,Mapping,Policies},
                                                      Services/Consumers,
<!--#if (files)-->
                                                      Configuration/Options,
<!--#endif-->
                                                      Helpers}
<!--#if (localAuth)-->
  AppTemplate.Application.Auth          no Common/
                                        Features/{Accounts,Sessions,TwoFactor,Passkeys,ExternalLogins,
                                                  SecurityEvents}/{UseCases,Ports,Model/{Errors,Policies}}
<!--#endif-->
Src/Infrastructure/
  AppTemplate.Infrastructure.Core       Common/{Caching,Contexts,Options,Saving,Templating,Time}
                                        no Features/
  AppTemplate.Infrastructure.Persistence
                                        Common/{Contexts,Idempotency,Leases}
                                        Features/<F>/{Access/{Queries,Repositories,Stores},
                                                      Rows/{Configurations,Mapping,Models,Tracking},
<!--#if (files)-->
                                                      Adapters/{Checkpoints,Locks,Observability}}
<!--#endif-->
<!--#if (!files)-->
                                                      Adapters/{Observability}}
<!--#endif-->
                                        Migrations/
<!--#if (localAuth)-->
  AppTemplate.Infrastructure.Auth       Common/{Contexts,Options}
                                        Features/<F>/{Access/{Queries,Tables},
                                                      Adapters/{Directories,Providers,Services},
                                                      Configuration/{Options,Seeding},
                                                      Resources/Templates,
                                                      Rows/{Configurations,Models}}
                                        Migrations/
<!--#endif-->
  AppTemplate.Infrastructure.Email      Common/{Http,Smtp}       Features/<F>/, files side by side
<!--#if (files)-->
  AppTemplate.Infrastructure.Storage    Common/{Budgets,Factories,Options}
                                        Features/Files/{Access/Stores,
                                                        Adapters/{Inventories,Scanners,Transfers},
                                                        Configuration/Options}
<!--#endif-->
  AppTemplate.Infrastructure.InMemory   Common/{Email,Time}      Features/<F>/, files side by side
Src/Presentation/
  AppTemplate.Presentation.Core         Common/{Jobs,Localization,Observability,Outbound,Security}
                                        no Features/
  AppTemplate.Api                       Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,
                                                Idempotency,Jobs,Localization,Observability,OpenApi,
                                                Security}
                                        Features/<F>/{Composition,
<!--#if (externalAuth)-->
                                                      Configuration/Options,
<!--#endif-->
                                                      Contracts/{Mapping,Requests,Responses},
                                                      Controllers,Jobs}
<!--#if (presentationBlazorServer && localAuth)-->
  <Project>.<Name>                      Common/{Hosting,Security}
                                        Features/<F>/Composition
                                        Components/, wwwroot/: dotnet new blazor's, at bootstrap
<!--#endif-->
<!--#if (presentationBlazorServer && externalAuth)-->
  <Project>.<Name>                      Common/{Hosting,Security}
                                        Features/<F>/{Composition,Configuration/Options}
                                        Components/, wwwroot/: dotnet new blazor's, at bootstrap
<!--#endif-->
<!--#if (presentationBlazorServer && publicApi)-->
  <Project>.<Name>                      Common/{Hosting,Security}
                                        no Features/
                                        Components/, wwwroot/: dotnet new blazor's, at bootstrap
<!--#endif-->
<!--#if (presentationAvalonia && localAuth)-->
  <Project>.<Name>                      Common/{Hosting}
                                        no Features/
                                        Assets/, ViewModels/, Views/: dotnet new avalonia.mvvm's, at bootstrap
<!--#endif-->
<!--#if (presentationAvalonia && externalAuth)-->
  <Project>.<Name>                      Common/{Hosting,Sessions}
                                        no Features/
                                        Assets/, ViewModels/, Views/: dotnet new avalonia.mvvm's, at bootstrap
<!--#endif-->
<!--#if (dotnetClient)-->
Src/Clients/
  AppTemplate.Client                    Common/{Composition,Errors,Sessions}
                                        no Features/
                                        Generated/: Kiota's, from the OpenAPI reference
<!--#endif-->
<!--#if (anyWeb)-->
Src/Web/                                the pnpm workspace: apps/ and packages/
<!--#if (webBlazorWasm)-->
  apps/FrameworkSite                    the WebAssembly app, a .NET project: no Common/, no Features/
                                        Layout/, Pages/, wwwroot/: dotnet new blazorwasm's, at bootstrap
<!--#endif-->
<!--#endif-->
Tests/                                  one test project per source project, mirroring its folders,
                                        plus Architecture/, EndToEnd/, Integration/ and Load/
```

<!--#if (localAuth)-->
The API's `Features/Auth/` is one host feature over the six authentication features: one route
tree, one composition entry, `AddAuthFeature`, and one folder to delete for a host without local
accounts.
<!--#endif-->

## The worked example

<!--#if (todoLists)-->
A rich feature's application folder, `Src/Application/AppTemplate.Application/Features/TodoLists/`:

```
UseCases/Commands/CreateTodoList/   CreateTodoListCommand, ICreateTodoListUseCase,
                                    CreateTodoListUseCase, CreateTodoListCommandValidator
UseCases/Queries/GetTodoList/       the same shape on the read side
Ports/TodoListQueries/              ITodoListQueries, and TodoListFilter that crosses it
Model/Dtos/                         the read models several operations return
Model/Errors/                       TodoListErrors, the feature's failure codes
Model/Mapping/                      TodoListDtoMapping, from aggregate to DTO
Model/Policies/                     TodoListCollectionPolicy, the sortable whitelist
Services/                           ITodoListService, the gate every command loads through
Services/Consumers/AccountDeleted/  one folder per event the feature answers
Helpers/                            TodoListItemExtensions
```
<!--#endif-->
<!--#if (!todoLists)-->
A rich feature's application folder, `AppTemplate.Application/Features/<Feature>/`:

```
UseCases/Commands/<Operation>/      the command, its named interface, the use case, its validator
UseCases/Queries/<Operation>/       the same shape on the read side
Ports/<Aggregate>Queries/           the queries port, and the filter that crosses it
Model/Dtos/                         the read models several operations return
Model/Errors/                       the feature's failure codes
Model/Mapping/                      from aggregate to DTO
Model/Policies/                     the sortable whitelist
Services/                           the gate every command loads its aggregate through
Services/Consumers/<Event>/         one folder per event the feature answers
```
<!--#endif-->

<!--#if (notes)-->
A simple feature has no operation folders, so an input DTO's validator sits beside the DTO:

```
Src/Application/AppTemplate.Application/Features/Notes/
  Model/Dtos/            NoteDto, NoteContentDto, NoteContentDtoValidator, …
  Model/Policies/        NoteInputPolicy, NoteCollectionPolicy
  Ports/NoteQueries/     INoteQueries, NoteFilter
  Ports/NoteStore/       INoteStore, NoteChangeStatus
  Services/              INoteService, NoteService
  Services/Consumers/    DeleteNotesOnAccountDeletedConsumer
Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Notes/
  Rows/Models/           NoteRecord, the only model
  Access/Queries/        NoteQueries, NoteSortMap
  Access/Stores/         NoteStore
```
<!--#endif-->

What serves one operation stays in its folder, what serves one port stays with the port, and what
serves several moves up to the concept folders.

## Naming

A type's suffix names its nature, and the nature names its folder:

| Suffix | Folder |
|---|---|
| `…UseCase`, `…Command`, `…Query`, an operation's `…Validator` | `UseCases/{Commands,Queries}/<Operation>/` |
| a port's interface and the messages that cross it, `…Outcome`, `…Status` | `Ports/<Port>/` |
| `…Repository` | `Repositories/` in the domain, `Access/Repositories/` for the adapter |
| `…Event` | `Model/Events/` |
| `…Dto`, an input DTO's `…Validator` | `Model/Dtos/` |
| `…Mapping` (to a DTO), `…Policy`, `…Errors` | `Model/Mapping/`, `Model/Policies/`, `Model/Errors/` |
| `…Service` (application), `…Consumer` | `Services/`, `Services/Consumers/` |
| `…Helpers`, `…Extensions` | `Helpers/` |
| `…Store`, `…Queries` with its `…Map`, `…Table` with its `…Grant` | `Access/Stores/`, `Access/Queries/`, `Access/Tables/` |
| `…Record`, `…Configuration`, `…Mapper`, `…Tracker` | `Rows/Models/`, `Rows/Configurations/`, `Rows/Mapping/`, `Rows/Tracking/` |
| `…Service`, `…Factory`, `…Issuer`, `…Log`, `…Verifier` (infrastructure) | `Adapters/Services/` |
| `…Provider`, `…Directory`, `…Scanner`, `…Inventory`, `…Transfer`, `…Lock`, `…Checkpoint` | the matching folder under `Adapters/` |
| `…Diagnostics`, `…Instruments` | `Adapters/Observability/`; a job's, `Jobs/` |
| `…Options`, `…Seeder` | `Configuration/Options/`, `Configuration/Seeding/`; a job's options, `Jobs/` |
| `…Request`, `…Response`, `…Mapping` (wire) | `Contracts/Requests/`, `Contracts/Responses/`, `Contracts/Mapping/` |
| `…Controller` | `Controllers/` |
| `…BackgroundService`, `…Task`, `…Telemetry` | `Jobs/` |
| `…Composition`, a host-supplied `…Directory`, `…Provider` or `…Session` | `Composition/` |

A port carries the nature word and its adapter is the port without its `I`: `IIdempotencyStore` is
implemented by `IdempotencyStore`. A prefix is right when several adapters implement one port
(`MailKitEmailSender`, `PostmarkEmailSender`) or when the technology shows at the call site
(`EfUnitOfWork`, `LeaderLease`). A port's folder keeps the capability name,
`Ports/<Capability>/`, never the interface's: a namespace and a type of one name make every consumer
ambiguous (CS0118).

A word names one notion. `Manager`, `Utils`, `Processor`, a bare `Handler` and `Composer` name
"code" rather than a thing and are not used. `Helpers` holds static classes of pure functions, only
when no more precise nature fits; a `Common/` has none, and a shared helper lives with its subject,
as `ResultExtensions` lives in `Common/Results/`.

## Adding a project

A project under `Src/` fails the build until `LayoutConventionTests` gives it an entry in both of its
vocabularies, the `Common/` one and the `Features/` one, and this page's folder map lists its words.
An entry is `null` when the project has no such folder, checked in both directions, and an empty
list when its features hold their files side by side.

A `.Core` project is marked `IsSdkProject`; an infrastructure project is a module only a host
references.

## The rules the tests hold

| Rule | Test |
|---|---|
| A folder even for a single file; a project root holds only its composition file | `LayoutConventionTests.NoProjectRoot_HoldsAnythingButItsModule` |
| No empty folder | `LayoutConventionTests.NoFolderInTheSourceTree_IsEmpty` |
| Namespaces follow folders, without exception | `LayoutConventionTests.EverySourceFile_DeclaresTheNamespaceOfItsFolder` |
| One public type per file, named for the file | `LayoutConventionTests.EverySourceFile_DeclaresOnePublicType_NamedForTheFile` |
| The `Common/` and `Features/` words are closed per project | `LayoutConventionTests.EveryCommonFolder_IsNamedFromItsProjectsVocabulary`, `EveryFeatureFolder_IsNamedFromItsLayersVocabulary` |
| Every project has a vocabulary | `LayoutConventionTests.EveryProjectOnDisk_HasAVocabularyOfItsOwn` |
| A file carries its folder's suffix; every exemption names a file | `FeatureFolderVocabularyTests.EveryFileUnderAFeature_CarriesItsFoldersNatureWord`, `EveryExemption_StillNamesAFile` |
| Every type under `Helpers/` is a static class | `FeatureFolderVocabularyTests.EveryTypeUnderAHelpersFolder_IsAStaticClass` |
| One use case per operation folder, named for it | `UseCaseConventionTests.EveryUseCaseFolder_HoldsOneUseCase_AndIsNamedForIt` |
| A repository contract lives in the domain | `StorageVocabularyTests.EveryRepositoryContract_IsDeclaredInTheDomain` |
| No `Common/` names a feature | `ModuleDependencyTests.EveryModuleCommon_KnowsNoFeature`, `HttpCommonBoundaryTests.TheCommonHttpCode_NamesNoFeature` |
