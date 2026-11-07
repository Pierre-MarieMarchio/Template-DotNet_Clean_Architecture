---
type: reference
title: Project layout
description: Where a file goes in this project and what it is called.
---

# Project layout

Where a file goes and what it is called. Every rule on this page is held by a test in
`Tests/Architecture/AT.Architecture.Tests`, so a misplaced file fails a test there. Why the
layers are drawn this way is [the architecture](../architecture/README.md).

## The folder map

A project's folder is its name with this project's name replaced by its initials, `<Initials>` in
the pages: the capital letters of the name in order, or a part's first letter where the part has
none, so a product named Acme.Shop keeps its project Acme.Shop.Application.Auth in
Src/Application/AS.Application.Auth. A module promoted to a project of its own follows the rule, and
a second product goes in a root folder of its own, since the tool finds a project by its
`cleanarch.json` ([ADR 0029](../decisions/0029-a-project-folder-carries-the-products-initials-not-its-name.md)).

Every project has the same shape: `Common/` for what knows no feature, `Features/<Feature>/` for the
rest, and a root that holds only the `.csproj`, one composition file (`Program.cs` or the project's
`…Module.cs`) and the folders `Common/`, `Features/`, `Migrations/` and `Properties/`.
<!--#if (presentationBlazorServer)-->
The portal's root also holds `Components/` and `wwwroot/`, which its framework's
template writes and which are its own from then on.
<!--#endif-->
<!--#if (presentationAvalonia)-->
The desktop app's root also holds `Assets/`, `ViewModels/`, `Views/`, `App.axaml.cs` and
the view locator, which its framework's template writes and which are its own from then on; a file
named `<Type>.axaml.cs` declares `<Type>`.
<!--#endif-->
<!--#if (presentationMcp)-->
The MCP server keeps each feature's tools under `Features/<F>/Tools`, one class named
`<F>Tools`, and what every tool shares under `Common/Tools`.
<!--#endif-->
<!--#if (presentationWpf)-->
The desktop app's root also holds `ViewModels/`, and `App.xaml.cs` and `MainWindow.xaml.cs` beside
their markup, in the shape `dotnet new wpf` gives them; a file named `<Type>.xaml.cs` declares
`<Type>`.
<!--#endif-->
<!--#if (dotnetClient)-->
The .NET client's root also holds `Generated/`, which `dotnet cleanarch openapi`
writes and nobody edits ([ADR 0023](../decisions/0023-a-dotnet-client-generated-by-kiota.md)).
<!--#endif-->

A brace lists every word a project allows at that level; a feature holds only the words it has
content for.

```
Src/Domain/
<!--#if (reminders || files)-->
  AT.Domain.Core                        Common/{Abstractions,Events,Exceptions,Primitives,Retries}
<!--#endif-->
<!--#if (!(reminders || files))-->
  AT.Domain.Core                        Common/{Abstractions,Events,Exceptions,Primitives}
<!--#endif-->
                                        no Features/
<!--#if (tagging)-->
  AT.Domain                             Common/{Tagging}
<!--#endif-->
<!--#if (!tagging)-->
  AT.Domain                             no Common/
<!--#endif-->
                                        Features/<F>/{Model/{Entities,Events,ValueObjects},Repositories}
Src/Application/
  AT.Application.Core                   Common/{Collections,Concurrency,Events,Idempotency,Localization,
                                                Policies,Ports,Results,UseCases,Validation}
                                        Features/<F>/UseCases/Commands/<Operation>
<!--#if (tagging)-->
  AT.Application                        Common/{Tagging}
<!--#endif-->
<!--#if (!tagging)-->
  AT.Application                        no Common/
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
  AT.Application.Auth                   no Common/
                                        Features/{Accounts,Sessions,TwoFactor,Passkeys,ExternalLogins,
                                                  SecurityEvents}/{UseCases,Ports,Model/{Errors,Policies}}
<!--#endif-->
Src/Infrastructure/
  AT.Infrastructure.Core                Common/{Caching,Contexts,Options,Saving,Templating}
                                        no Features/
  AT.Infrastructure.Persistence
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
  AT.Infrastructure.Auth                Common/{Contexts,Options}
                                        Features/<F>/{Access/{Queries,Tables},
                                                      Adapters/{Directories,Providers,Services},
                                                      Configuration/{Options,Seeding},
                                                      Resources/Templates,
                                                      Rows/{Configurations,Models}}
                                        Migrations/
<!--#endif-->
  AT.Infrastructure.Email               Common/{Http,Smtp}       Features/<F>/, files side by side
<!--#if (files)-->
  AT.Infrastructure.Storage             Common/{Budgets,Factories,Options}
                                        Features/Files/{Access/Stores,
                                                        Adapters/{Inventories,Scanners,Transfers},
                                                        Configuration/Options}
<!--#endif-->
  AT.Infrastructure.InMemory            Common/Email             Features/<F>/, files side by side
Src/Presentation/
  AT.Presentation.Core                  Common/{Jobs,Localization,Observability,Outbound,Security}
                                        no Features/
<!--#if (dotnetClient)-->
  AT.Presentation.Client                Common/{Composition,Errors,Sessions}
                                        no Features/
                                        Generated/: Kiota's, from the OpenAPI reference
<!--#endif-->
<!--#if (api && !jobsWorker)-->
  AT.Api                                Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,
                                                Idempotency,Jobs,Localization,Observability,OpenApi,
                                                Security}
                                        Features/<F>/{Composition,
<!--#if (externalAuth)-->
                                                      Configuration/Options,
<!--#endif-->
                                                      Contracts/{Mapping,Requests,Responses},
                                                      Controllers,Jobs}
<!--#endif-->
<!--#if (api && jobsWorker)-->
  AT.Api                                Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,
                                                Idempotency,Localization,Observability,OpenApi,
                                                Security}
                                        Features/<F>/{Composition,
<!--#if (externalAuth)-->
                                                      Configuration/Options,
<!--#endif-->
                                                      Contracts/{Mapping,Requests,Responses},
                                                      Controllers}
<!--#endif-->
<!--#if (presentationBlazorServer && localAuth)-->
  <Initials>.Portal                     Common/{Hosting,Security}
                                        Features/<F>/Composition
                                        Components/, wwwroot/: dotnet new blazor's, at bootstrap
<!--#endif-->
<!--#if (presentationBlazorServer && externalAuth)-->
  <Initials>.Portal                     Common/{Hosting,Security}
                                        Features/<F>/{Composition,Configuration/Options}
                                        Components/, wwwroot/: dotnet new blazor's, at bootstrap
<!--#endif-->
<!--#if (presentationBlazorServer && publicApi)-->
  <Initials>.Portal                     Common/{Hosting,Security}
                                        no Features/
                                        Components/, wwwroot/: dotnet new blazor's, at bootstrap
<!--#endif-->
<!--#if (presentationAvalonia && localAuth)-->
  <Initials>.Desktop                    Common/{Hosting}
                                        no Features/
                                        Assets/, ViewModels/, Views/: dotnet new avalonia.mvvm's, at bootstrap
<!--#endif-->
<!--#if (presentationAvalonia && externalAuth)-->
  <Initials>.Desktop                    Common/{Hosting,Sessions}
                                        no Features/
                                        Assets/, ViewModels/, Views/: dotnet new avalonia.mvvm's, at bootstrap
<!--#endif-->
<!--#if (presentationMcp && localAuth)-->
  <Initials>.Mcp                        Common/{Hosting,Security,Tools}
                                        Features/<F>/{Composition,Tools}
<!--#endif-->
<!--#if (presentationMcp && externalAuth)-->
  <Initials>.Mcp                        Common/{Hosting,Security,Tools}
                                        Features/<F>/{Composition,Configuration/Options,Tools}
<!--#endif-->
<!--#if (presentationWpf && localAuth)-->
  <Initials>.Desktop                    Common/{Hosting}
                                        no Features/
                                        ViewModels/; App.xaml.cs, MainWindow.xaml.cs: dotnet new wpf's shape
<!--#endif-->
<!--#if (presentationWpf && externalAuth)-->
  <Initials>.Desktop                    Common/{Hosting,Sessions}
                                        no Features/
                                        ViewModels/; App.xaml.cs, MainWindow.xaml.cs: dotnet new wpf's shape
<!--#endif-->
<!--#if (jobsWorker)-->
  <Initials>.Worker                     Common/{Jobs,Security}
                                        Features/<F>/{Composition,Jobs}
<!--#endif-->
<!--#if (aspire)-->
Src/AppHost/
  AT.AppHost                            Common/{Hosting}
                                        no Features/; AppHostModule.cs at its root: the app host's entry
<!--#endif-->
<!--#if (anyWeb)-->
Src/Web/                                the pnpm workspace: apps/ and packages/
<!--#if (webBlazorWasm && !externalAuth)-->
  apps/FrameworkSite                    the WebAssembly app, a .NET project: no Common/, no Features/
                                        Layout/, Pages/, wwwroot/: dotnet new blazorwasm's, at bootstrap
<!--#endif-->
<!--#if (webBlazorWasm && externalAuth)-->
  apps/FrameworkSite                    the WebAssembly app, a .NET project: Common/{Hosting,Sessions}, no Features/
                                        Layout/, Pages/, wwwroot/: dotnet new blazorwasm's, at bootstrap
<!--#endif-->
<!--#endif-->
Tests/                                  one test project per source project, mirroring its folders,
                                        plus Architecture/, EndToEnd/, Integration/ and Load/
```

## The project file

`cleanarch.json`, at the root, records what this project was generated with and what was added
since. `dotnet cleanarch` finds the root by it and reads the options there; `add feature`
reads `database` to write the test of the feature's table, which a schema holds on PostgreSQL and SQL
Server and which carries the schema in its name on SQLite.

| Key | Holds |
|---|---|
| `template` | the `package` and the `version` that generated the project |
| `name` | the project's name, the prefix of every project under `Src` and `Tests` |
| `options` | each generation option that adds no part: `auth`, `database`, `examples`, `files`, `ci`, `sonar`, `agentFiles`, `aspire` |
| `client` | `true` when the project holds the .NET client of the API |
| `web` | present with the web workspace: its npm `scope`, its `apps` with the `framework`, `audience`, `rendering` and host `port` each was added with, and the `packages` added to it |
| `presentation` | every presentation project under `Src/Presentation` but `Presentation.Core` and the .NET client, the API and the worker among them: its `name`, the part of its project's name after this project's name, and its `framework` |

The options are a fact about the day of generation, and no command rewrites them: removing an example
feature leaves `examples` as it was. A part added later is recorded by the command that joins it,
`add web-app` for a web app or package and `add presentation` for a presentation project. A key this
table does not name is kept by every command that writes the file. `ProjectFileTests` fails when the
file and the tree disagree.
<!--#if (localAuth && api)-->

The API's `Features/Auth/` is one host feature over the authentication features: one route
tree, one composition entry, `AddAuthFeature`, and one folder to delete for a host without local
accounts.
<!--#endif-->
<!--#if (localAuth && !api)-->

Each host's `Features/Auth/` is one host feature over the authentication features: one composition
entry and one folder to delete for a host without local accounts.
<!--#endif-->

## The worked example

<!--#if (todoLists)-->
A rich feature's application folder, `Src/Application/AT.Application/Features/TodoLists/`:

```
UseCases/Commands/CreateTodoList/   CreateTodoList.cs: CreateTodoListCommand and its
                                    CreateTodoListCommandHandler; CreateTodoListCommandValidator
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
A rich feature's application folder, `AT.Application/Features/<Feature>/`:

```
UseCases/Commands/<Operation>/      <Operation>.cs, the command and its handler; its validator
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
Src/Application/AT.Application/Features/Notes/
  Model/Dtos/            NoteDto, NoteContentDto, NoteContentDtoValidator, …
  Model/Policies/        NoteInputPolicy, NoteCollectionPolicy
  Ports/NoteQueries/     INoteQueries, NoteFilter
  Ports/NoteStore/       INoteStore, NoteChangeStatus
  Services/              INoteService, NoteService
  Services/Consumers/    DeleteNotesOnAccountDeletedConsumer
Src/Infrastructure/AT.Infrastructure.Persistence/Features/Notes/
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
| `…Command` and `…CommandHandler`, `…Query` and `…QueryHandler`, an operation's `…Validator` | `UseCases/{Commands,Queries}/<Operation>/`, the message and its handler together in `<Operation>.cs` |
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
| <!--#if (presentationMcp)--> |
| `…Tools`, the MCP server's tools of one feature | `Tools/` |
<!--#endif-->

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

A project under `Src/` fails `LayoutConventionTests` until that test gives it an entry in both of its
vocabularies, the `Common/` one and the `Features/` one, and this page's folder map lists its words.
An entry is `null` when the project has no such folder, checked in both directions, and an empty
list when its features hold their files side by side.

[Adding a project](../guides/adding-a-project.md) walks through every step.
A `.Core` project is marked `IsSdkProject`; an infrastructure project is a module only a host
references. Its folder takes the initials, as [the folder map](#the-folder-map) says, and
`ProjectFolderTests` names the folder a misplaced project belongs in.

Each project under `Src/` has one unit test project, `<Name>.UnitTests`, in the same layer folder under
`Tests/`, and `TestProjectMirrorTests` names the one that is missing. An app of the web workspace,
the output of an official CLI, goes without one.
<!--#if (aspire)-->
So does the app host, which `LocalStackTests` starts from its integration test project.
<!--#endif-->

## The rules the tests hold

| Rule | Test |
|---|---|
| `cleanarch.json` records the name, the options and the parts the tree holds | `ProjectFileTests` |
| A folder even for a single file; a project root holds only its composition file | `LayoutConventionTests.NoProjectRoot_HoldsAnythingButItsModule` |
| No empty folder | `LayoutConventionTests.NoFolderInTheSourceTree_IsEmpty` |
| Namespaces follow folders, without exception | `LayoutConventionTests.EverySourceFile_DeclaresTheNamespaceOfItsFolder` |
| One public type per file, named for the file; a use case's own file holds its message and that message's handler | `LayoutConventionTests.EverySourceFile_DeclaresOnePublicType_NamedForTheFile` |
| The `Common/` and `Features/` words are closed per project | `LayoutConventionTests.EveryCommonFolder_IsNamedFromItsProjectsVocabulary`, `EveryFeatureFolder_IsNamedFromItsLayersVocabulary` |
| Every project has a vocabulary | `LayoutConventionTests.EveryProjectOnDisk_HasAVocabularyOfItsOwn` |
| A project sits in its layer's folder, in `<Initials>.<rest of its name>` | `ProjectFolderTests.EveryProject_SitsInTheFolderItsNameGives` |
| A file carries its folder's suffix; every exemption names a file | `FeatureFolderVocabularyTests.EveryFileUnderAFeature_CarriesItsFoldersNatureWord`, `EveryExemption_StillNamesAFile` |
| Every type under `Helpers/` is a static class | `FeatureFolderVocabularyTests.EveryTypeUnderAHelpersFolder_IsAStaticClass` |
| One use case per operation folder, named for it | `UseCaseConventionTests.EveryUseCaseFolder_HoldsOneUseCase_AndIsNamedForIt` |
| Every message has one handler | `MessageRoutingTests.EveryMessage_HasExactlyOneHandler` |
| Every message is sent by a host or a job, under a short name no other message has, and crosses the validation behaviour | `MessageRoutingTests.EveryMessage_IsSentByAHostOrAJob`, `NoTwoMessages_ShareAShortName`, `ContainerCompositionTests.EveryMessage_PassesThroughTheValidationBehaviour` |
| Every validator targets a message or is taken by the service that runs it | `ValidatorTargetTests.EveryValidator_TargetsAMessageOrIsInjectedWhereItRuns` |
| Every source project has its unit test project, but the exceptions the test names | `TestProjectMirrorTests.EverySourceProject_HasItsUnitTestProject_ExceptTheOnesNamedHere` |
| MediatR, AutoMapper and Mapperly are in no project | `BannedPackageTests.NoProject_ReferencesABannedPackage` |
| A repository contract lives in the domain | `StorageVocabularyTests.EveryRepositoryContract_IsDeclaredInTheDomain` |
| No `Common/` names a feature | `ModuleDependencyTests.EveryModuleCommon_KnowsNoFeature`, `HttpCommonBoundaryTests.TheCommonHttpCode_NamesNoFeature` |
