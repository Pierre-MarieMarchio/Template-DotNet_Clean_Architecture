# Modules and boundaries

A feature may reach another only through that feature's ports or its domain events, and an
infrastructure module may reach no other module. These rules keep every feature and every module
removable, and the architecture tests hold each one.

## Infrastructure per capability

Each capability is one project, with one registration call and, where it stores data, one context
over the schemas it owns. A module holds all the technology of its capability: the email module
carries both its transports, chosen by configuration, rather than a project per transport. The seam
that buys portability is the application port the module implements, one layer in; a second
project below it would be an interface under the interface that matters.

A module references the application projects whose ports it implements and
`AppTemplate.Infrastructure.Core`. It never references a sibling, and
`AppTemplate.Infrastructure.Persistence` references no module either. Anything two modules both need
is a port in the application layer or a mechanism in `AppTemplate.Infrastructure.Core`.
`InfrastructureModules_ReferenceOnlyTheLayersCoreHorizontally` and
`NoInfrastructureModule_DependsOnAnotherInfrastructureModule` hold it.

The word "Module" names one thing only: a project's registration class, such as `PersistenceModule`
or `HttpPipelineModule`. It is a composition concept, never a business partition.

## The modules

| Module | Registers | Storage |
|---|---|---|
| `AppTemplate.Infrastructure.Persistence` | `AppDbContext`, `IUnitOfWork`, `ILeaderLease`, the business features' repositories, queries and stores, the idempotency store | `AppDbContext`: a schema per business feature, plus `platform` |
<!--#if (localAuth)-->
| `AppTemplate.Infrastructure.Auth` | the authentication ports over ASP.NET Core Identity, JWT bearer, refresh tokens, the data-protection key ring, the development seed | `AuthDbContext`: the `identity` schema |
<!--#endif-->
<!--#if (reminders)-->
| `AppTemplate.Infrastructure.Email` | `IEmailSender` over SMTP or Postmark, `IReminderNotifier` | none |
<!--#endif-->
<!--#if (!reminders)-->
| `AppTemplate.Infrastructure.Email` | `IEmailSender` over SMTP or Postmark | none |
<!--#endif-->
<!--#if (files)-->
| `AppTemplate.Infrastructure.Storage` | the file content store, direct transfer, inventory and scanner | an S3-compatible bucket or a local directory |
<!--#endif-->
| `AppTemplate.Infrastructure.InMemory` | doubles a test composes over a real module | none |

<!--#if (externalAuth)-->
The mapping of an external provider's subjects to owners is a business feature of the persistence
module, in the `external_identity` schema, composed by `AddExternalIdentityPersistence`.
<!--#endif-->

The in-memory module is the one no host composes; see
[Ports and adapters](ports-and-adapters.md#the-in-memory-module).

## Features meet through ports and events

A feature uses another only through that feature's public application port or its domain events,
never through its records, its EF configurations, its sets or its tables. The surface of a feature
is two namespaces: `Ports` in the application project and `Model/Events` in the domain project.

The reason is extraction: each reference that reaches past a port is an edit the remaining feature
would need before the other could leave. `FeatureIsolationTests` reads both the application and the
persistence project, because a query can read another feature's rows without any signature showing
it.

<!--#if (reminders)-->
`Reminders` needs to know that a to-do item exists, who owns it and whether it is done. It asks
`ITodoItemQueries`, a narrow port `TodoLists` declares and implements, and never reads the to-do
rows itself. `AddReminders()` therefore needs `AddTodoLists()` in the same host.
<!--#endif-->

## Two Common/ folders

Every project may have a `Common/` beside its `Features/`, and one question sorts a type between
them: **does it know a feature?**

| Folder | Holds | Example |
|---|---|---|
| `<Layer>.Core/Common/` | mechanisms agnostic of the business; nothing in it may ever name a feature | `Result`, `PageRequest`, `IUnitOfWork`, `AggregateRoot`, `UserId` |
| `<Layer>/Common/` | what several features share as business | a value object two aggregates own |
| the API's `Common/` | the HTTP mechanisms every feature of the host shares | the controller base, problem details, preconditions, idempotency |
| a module's `Common/` | what serves the module itself | its context, its shared options |

<!--#if (tagging)-->
`Common/Tagging/`, in both business projects, is the worked case: `TagSet` names the business type
`Tag`, so it cannot sit in a `.Core` project, and every tagged aggregate owns one.
<!--#endif-->
<!--#if (!tagging)-->
No business `Common/` exists yet: the first rule two features share that names one of their types
earns the domain or application project its first one.
<!--#endif-->

A folder in the business `Common/` is earned by naming a business type, not by being repeated: the
ownership check and the paged read every feature performs name no feature, so they live one project
inwards. `EveryModuleCommon_KnowsNoFeature`, `ThePersistenceMechanisms_KnowNoFeature` and
`TheCommonHttpCode_NamesNoFeature` hold the rule.
<!--#if (anyFeature)-->
`AppDbContext` is its one exception: it applies every feature's configuration, and
`TheContext_IsTheOneThingInCommonThatNamesAFeature` asserts that it really does.
<!--#endif-->

## Inside a feature

Under `Features/<Feature>/`, every path answers four questions in order:

| Level | Folder | Question |
|---|---|---|
| 1 | `Common/` or `Features/<Feature>/` | does it know a feature? |
| 2 | a concept: `UseCases/`, `Ports/`, `Model/`, `Access/`, `Rows/`, … | what role does it play? |
| 3 | a nature: `Commands/`, `Dtos/`, `Repositories/`, `Models/`, … | what is it? |
| 4 | a subject: `<Operation>/`, `<Port>/`, `<Event>/` | which one? |

A concept folder exists only when it has content. What serves one operation stays in that
operation's folder, what serves one port stays with the port, and what serves several is promoted to
the concept folders. The vocabulary is closed per project and checked in both directions;
[the project layout](../reference/project-layout.md) lists every word, the suffix each one takes, and
the test that holds it.

Every module keeps the same partition, `Common/` plus `Features/<Feature>/`, whether it serves one
feature or several, so the tree says which files leave with a feature.

## Decisions

- [0004. Features meet through ports and events](../decisions/0004-features-meet-through-ports-and-events.md)
- [0003. Use cases and ports are explicit](../decisions/0003-use-cases-and-ports-are-explicit.md)
