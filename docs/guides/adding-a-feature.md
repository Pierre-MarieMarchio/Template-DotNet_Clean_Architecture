---
type: guide
title: Adding a feature
description: How to add a feature of your own, in the shape its rules call for, with its tests and its page.
---

# Adding a feature

## When

The product needs a capability of its own: its records, its rules, its routes. Decide its shape first,
with the one question [feature shapes](../architecture/feature-shapes.md) asks: does the feature have
rules that protect a state?

<!--#if (publicApi)-->
This project was generated with `--auth none`, and `add feature` refuses it: both shapes it writes
need an authenticated caller. Add an authentication scheme and an `ICurrentUser` that reads it first,
or write a public feature by hand from the steps below.

<!--#endif-->
## Generate it

<!--#if (canAddFeature)-->
```bash
dotnet cleanarch add feature <Features> <Feature> --access owner|shared \
    [--deletion delete|anonymise] [--operations get,list,create,update,delete] \
    [--model record|aggregate] [--concurrency none|etag] [--fields "<Name:type>, ..."] [--dry-run]
```

<!--#endif-->
<!--#if (!canAddFeature)-->
In a project that authenticates its callers, the generator takes the options below; here they
describe what a feature written by hand holds.

<!--#endif-->
`<Features>` is the folder, plural (`Orders`); `<Feature>` the type, singular (`Order`).

| Option | Default | Meaning |
|---|---|---|
| `--access` | required | `owner`: every operation is scoped to the caller, and another caller's record answers like a missing one. `shared`: every authenticated caller reaches the same records |
| `--deletion` | required with `owner` | the fate of the rows when their owner's account is deleted: `delete` or `anonymise`. Refused with `shared` |
| `--operations` | all five | any subset of `get`, `list`, `create`, `update`, `delete`; `create` needs `get` |
| `--model` | `record` | `record`: a service over a store, no domain model. `aggregate`: a domain aggregate and one use case per operation |
| `--concurrency` | `none` | `none`: last write wins. `etag`: a persisted version, `ETag` on reads, `If-Match` on writes. `aggregate` needs `etag` |
| `--fields` | `Name`, `Description` | the feature's fields, as the grammar below reads them |
| `--dry-run` | off | print the files, the edits and the commands, and write and run nothing |

Every refused combination is refused before anything is written, with the change that fixes it.

### Fields

<!--#if (canAddFeature)-->
```bash
dotnet cleanarch add feature Projects Project --access owner --deletion delete \
    --fields "Title:string(120), Notes:text?, Budget:decimal(12,2)>=0, Due:DateOnly"
```

<!--#endif-->
`--fields` lists the fields as `Name:type`, comma separated. A name is PascalCase; a type is one of
this closed set, and anything else is refused with the character where reading stopped and what it
expected:

| Type | Holds | Column |
|---|---|---|
| `string(n)` | a line of 1 to `n` characters once trimmed, `n` from 1 to 400 | `n` characters |
| `text`, `text(n)` | free text, which may be empty, up to `n` characters: 2,000 by default, 65,536 at most | `n` characters |
| `int`, `long` | a whole number | the provider's integer |
| `decimal`, `decimal(p,s)` | an exact number of `p` digits, `s` after the point: (18,2) by default, 28 digits at most | precision `p`, scale `s` |
| `bool` | true or false | the provider's boolean |
| `DateOnly` | a date, sent as `yyyy-MM-dd` | a date |
| `DateTimeOffset` | an instant, kept in UTC whatever offset it was sent with | the provider's instant |
| `Guid` | an identifier, never the empty one when required | the provider's identifier |

`?` after the type makes the field nullable, and adds `null` to what it accepts and nothing else.
`>=`, `<=`, `>` or `<` and a number bound an `int`, a `long` or a `decimal`, one lower and one upper
at most: `Count:int>=1<=10`. A date takes no bound, its usual rule being relative to the clock, which
belongs in the domain. An `enum`, a `DateTime`, a `double` and their kin are refused with the type to
use instead.

Every field is checked twice, by the validator and by the aggregate's invariant, or by the validator
the service runs for `--model record`, with the same message; a string or a text is bounded in both
and in its column, so no column is unbounded. A name that is a C# keyword once lowered, a member the
feature already declares (`Id`, `OwnerId`, `Version`, the audit stamps), a type the generated code
calls by name, or another field's name with a case change or a generated suffix is refused.

The list sorts by `createdAt` and by each required `string(n)`, `int`, `long`, `DateOnly` and
`DateTimeOffset` field, each with its index; a `text` is long, SQLite orders no `decimal`, a `bool`
or a `Guid` sorts nothing useful, and a nullable field orders its nulls apart on each provider. A
cursor carries a string or an instant; a number or a date sorts by page only. `--deletion anonymise`
writes `[deleted account]` in each required `string(n)`, an empty text in each required `text` and
`null` in each nullable field, and keeps the others, which the command names.

The command writes:

- the feature's folders in each layer, and tests that hold for any feature of its shape, or, with
  `--fields`, tests of each field's rules in the validator, the aggregate and the mapper;
- `.feature-manifest/<Features>.json`: the options, the SHA-256 of every file written, and each edit
  made, with the line it is placed beside, so the edit can be undone exactly;
- the feature page `docs/features/<features>.md`, from `docs/_page-templates/feature.md`.

<!--#if (!api)-->
This project holds no HTTP API, so it writes no controller, no contract and no API test: the hosts
offer the feature through its use cases.

<!--#endif-->
Then it makes the edits that compose the feature: the schema, set and configuration in
`AppDbContext`, an `Add<Features>()` in `ApplicationModule`, the adapters in `PersistenceModule`, the
call in `Program.cs`, and the two test compositions, with the usings each needs. It then runs
`docs-index`, which lists the feature's page in the index of `docs/features/` by its frontmatter.
<!--#if (api)-->
Its section of the route table and the row of its refusal go to `docs/reference/api.md`. With
`--fields`, it also writes an integration test that creates a resource with every field and sees an
invalid one answered `400`, and adds the feature's schema to those `TestDatabase` truncates between
tests.
<!--#endif-->
An owner feature that is the project's first makes five more, for the account-deletion sweep it brings
with it. Each edit is placed beside a line the command finds, and every one is checked before anything
is written: when a line is missing because the file changed, it writes nothing, prints the edits to
make by hand and names the one that failed. An edit already in place counts as made, never repeated.
<!--#if ((presentationBlazorServer || presentationMcp) && api)-->
Each host composes itself, so the call goes in the `Program.cs` of the portal or the MCP server too, and a first
owner feature writes and registers each host's account directory beside the API's; the edits name
both ([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
<!--#endif-->
<!--#if (!api)-->
Each host composes itself, so the call goes in every host's `Program.cs`, and a first owner feature
writes and registers each host's account directory; the edits name every host
([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
<!--#endif-->
<!--#if (presentationMcp)-->
The feature brings no tool: the MCP server offers a use case only once its tool is declared by hand,
as the [presentation projects](../features/presentation.md#declaring-a-tool) shows.
<!--#endif-->

Before it writes, it refuses while a context has no initial migration, or while
`dotnet cleanarch migrations add --check` finds a model change no migration holds, which the
feature's migration would otherwise carry. After it writes, it runs in this order, and stops at the
first that fails, naming what is done and the commands that remain:

1. `dotnet cleanarch build`, the one build of the run
2. `dotnet cleanarch migrations add Add<Features> --no-build`, on the business context
<!--#if (api)-->
3. `dotnet cleanarch openapi`, which also writes the .NET client when the project holds one and runs
   `pnpm api <reference>` in the web workspace when it holds one
<!--#endif-->

The migration step works from that build, so the model is read once. When a fix after a failed step
changes the code, build again before the steps that remain.

Run again with the same options, it writes nothing and resumes these steps; the migration it added
is not added twice.
<!--#if (accounts)-->
Why it makes the edits rather than print them is [ADR 0031](../decisions/0031-the-feature-generator-composes-what-it-writes.md).
<!--#endif-->

## What you write by hand

The generator writes the shape; the rules are yours. Each step below has a test that reads it, so run
`dotnet test --project tests/Architecture/AT.Architecture.Tests` before you push.

### Domain

For `aggregate` only: the invariants, in the factory and in the rehydration path alike, the value
objects, and the domain events the feature raises. [Domain and results](../architecture/domain-and-results.md)
says how a rule fails.

### Application

The validator's rules, the errors the feature can answer, and any use case beyond the five
operations, which [`add use-case`](#add-a-use-case) starts. A use case reaches another feature only
through that feature's ports and events, as [reaching another feature](reaching-another-feature.md)
shows.

### Persistence

The record's columns, indexes and constraints in its EF configuration. The store or the repository
stays one capability; [ports and adapters](../architecture/ports-and-adapters.md) names the shapes.

<!--#if (api)-->
### API

The request and response contracts, and anything beyond default-deny authorisation. The
[HTTP API](../architecture/http-api.md) conventions apply to every route.
<!--#endif-->
<!--#if (!api)-->
### Hosts

What each host shows of the feature, calling its service or its use cases, and anything beyond
default-deny authorisation.
<!--#endif-->

### Tests

<!--#if (api)-->
The rules the feature protects, each with a test you have seen fail, and the integration tests against
the project's database under `tests/Integration/AT.Api.IntegrationTests`;
[testing](testing.md#where-a-new-features-tests-go) says which type holds what. Add the feature's
schema to the schemas `TestDatabase` truncates between tests.
<!--#endif-->
<!--#if (!api)-->
The rules the feature protects, each with a test you have seen fail;
[testing](testing.md#where-a-new-features-tests-go) says which type holds what.
<!--#endif-->

### Migration

<!--#if (localAuth)-->
`add feature` added `Add<Features>` to the business context; authentication's is untouched. Each
column, index or constraint you add to the record's configuration then needs a migration of its own:
<!--#endif-->
<!--#if (!localAuth)-->
`add feature` added `Add<Features>` to the business context. Each column, index or constraint you
add to the record's configuration then needs a migration of its own:
<!--#endif-->

```bash
dotnet cleanarch migrations add <Name>
```

A migration a database has applied is never rewritten, as [migrations](migrations.md#add-a-migration)
says.
<!--#if (!publicApi)-->
A field of a feature `add feature --fields` generated is [`add field`'s](#add-a-field), which adds
its migration itself.
<!--#endif-->

## Add a field

<!--#if (!publicApi)-->
```bash
dotnet cleanarch add field <Features> "<Name:type>=<default>, ..." [--dry-run]
dotnet cleanarch add field Projects "Budget:decimal(12,2)>=0=0, Code:string(20)='none', Notes:text?"
```

`add field` adds one or more fields to a feature `add feature --fields` generated, in every member
`--fields` writes a field in, and leaves the feature as `add feature --fields` would have written it
with the new fields last. The fields read as [`--fields`](#fields) reads them, with one addition: a
field that cannot be null takes a default after `=`, the value the rows the table already holds
receive, which its rules must accept. A number, `true` or `false` is written bare; a string, a text,
a `DateOnly` (`'2026-01-31'`), a `DateTimeOffset` with its offset (`'2026-01-31T09:00:00Z'`) or a
`Guid` goes between quotes, a quote inside doubled. A nullable field takes none, and the rows hold
`null`.

It finds each member through the file's syntax tree, whatever its formatting, and leaves the members
a person added where they are. It refuses, and writes nothing, when the feature has no manifest,
being written by hand as the example features are, or was generated without `--fields`, whose `Name`
and `Description` are placeholders; when a member the field changes was changed by hand or is gone,
naming the file and the member with the lines to add and to remove; and when a field is there already
or breaks a rule `--fields` checks. Every change is made or none is.

<!--#if (api)-->
A field that cannot be null is a breaking change of the HTTP contract: once the API ships, a create
or an update without it answers `400`, so the command names it, and the clients it breaks ship with
the API. A nullable field breaks no client, but an update replaces the whole resource, so one that
leaves it out stores `null`: a client that does not know the field yet clears it.
<!--#endif-->
<!--#if (!api)-->
A field that cannot be null changes what the create and update operations take, and the command says
so: every caller passes it. A nullable field breaks no caller.
<!--#endif-->

Before it writes, it refuses as `add feature` does while a context has no initial migration or a
model change waits for one. After it writes, it builds once, adds the `Add<Field>To<Features>`
migration (`Add<Field>And<Field>To<Features>` for two fields, `Add<Field>And<N>MoreFieldsTo<Features>`
for more), sets each default as the `defaultValue` of the migration's `AddColumn`, which `dotnet ef`
writes as the type's empty value,
<!--#if (api)-->
then rewrites the OpenAPI reference and the clients written from it,
<!--#endif-->
and stops at the first step that fails; the same command resumes once the failure is fixed. The
manifest records each field with its default, the new SHA-256 of each file, and under `addedFields`
the declaration, the migration and each file's change, so `remove feature` reads the files as
untouched.
<!--#if (accounts)-->
Why a field is written through the syntax tree while the feature's edits are lines placed beside an
anchor is [ADR 0031](../decisions/0031-the-feature-generator-composes-what-it-writes.md).
<!--#endif-->

### By hand

A feature written by hand takes a field by hand, in each place `add field` reaches:
<!--#endif-->
<!--#if (publicApi)-->
This project authenticates nobody, so no feature here comes from `add feature`, and a field goes in
by hand, in each place below:
<!--#endif-->

- the aggregate: its property, its constants, its invariant in the factory, the rehydration and the
  update, with its message; or, for a record feature, the input policy's constants;
- the commands or the content DTO, the DTOs, the summary when it holds the field, the validator's
  rule with the same message, and the mapping from the aggregate or the record;
- the record's property, its column in the EF configuration with its length or precision, and the
  sort index and the collection policy's field when it sorts; the mapper both ways, the store's
  writes and the queries' projections;
<!--#if (api)-->
- the request contracts, with `[property: JsonRequired]` on a value type and the documentation of
  each parameter, the responses, their mapping and the controller's call;
<!--#endif-->
- the tests of the validator, the aggregate, the mapper and the configuration, and the integration
  test that creates a resource with the field;
- a migration, `dotnet cleanarch migrations add Add<Field>To<Features>`, whose `AddColumn`
  gives the rows already stored the `defaultValue` the field's rules accept.

## Add a use case

A feature of the rich shape grows one operation at a time:

```bash
dotnet cleanarch add use-case <Features> <UseCase> --kind command|query [--input id|none|"<Inputs>"]
```

`<UseCase>` names the operation, a verb and its subject: `Approve<Feature>`, `Get<Feature>Totals`.

| Option | Default | Meaning |
|---|---|---|
| `--kind` | required | `command`: changes state and answers a `Result`, under `UseCases/Commands/`. `query`: reads and answers a `Result` of a DTO named for it, under `UseCases/Queries/` |
| `--input` | `id` | What the message carries, comma separated. `id`: the id of the aggregate the feature's repository names, which the route carries, first when inputs follow it. `Name:Type`: an input in PascalCase, of any C# type, written as given. `none`: a message without input |

```bash
dotnet cleanarch add use-case <Features> Find<Features>ByName --kind query --input "Name:string, MaxCount:int"
dotnet cleanarch add use-case <Features> Retag<Feature> --kind command --input "id, Tags:IReadOnlyList<string>"
```

A type may be generic, nullable, an array or a tuple: `Dictionary<string, int>`, `DateOnly?`,
`Guid[]`. An input of a known type (`string`, `int`, `long`, `decimal`, `bool`, `Guid`,
`DateTimeOffset`, `DateOnly`) gets a rule, `NotEmpty`, which refuses its type's default, and a test
value; a nullable one and a `bool` accept every value, so they get a test value and no rule. Any
other type is written as given, with no rule, and its tests pass `default!` for it: the command lists
it, and its rule, its test value and the using of its namespace are yours to write.

The command writes in the application layer:

- the use case's folder: `<UseCase>.cs`, holding its command or query and that message's handler,
  and, when an input has a rule, the message's validator;
- for a query, its DTO under `Model/Dtos/`, holding the aggregate's `Guid Id` when the input holds
  `id` and nothing otherwise, unless the feature already declares one of that name;
- with the validator, its tests, where `AWellFormed<Command|Query>()` builds the one valid message
  and each rule has its refusal; and the handler's test, which proves a malformed message never
  reaches the handler.

The handler's test builds the handler in one place, `UseCase()`, behind its validator, as the
hand-written tests do. A dependency the handler gains is a substitute field of the test passed
there, and no test changes:

```csharp
private readonly I<Feature>Repository _repository = Substitute.For<I<Feature>Repository>();

private ValidatedHandler<<UseCase>Command, Result> UseCase() =>
    new(new <UseCase>CommandHandler(_repository).Handle, new <UseCase>CommandValidator());
```

Nothing is registered by hand: `AddFeature` in `ApplicationModule` finds the validator by folder, and
each host's generated mediator registers the handler, behind the use-case pipeline that runs the
validator first. When the feature has a manifest, the files join it, so `remove feature --apply`
removes them with the feature, edited or not.

The handler throws `NotImplementedException` until its body is written: the rule in the aggregate,
the load, the call and the commit, each decision with its test. Until a host sends its message,
`MessageRoutingTests` names it.
<!--#if (api)-->
Where the API serves the feature, the command also adds the action that sends the message over HTTP
to the feature's controller, with its usings and the `ISender` the controller lacks: the id travels in
the route, the other inputs in a request contract it writes, read from the query string for a query
and from the body for a command; and for a query it writes the response contract. It adds the
route's row to the feature's table in `docs/reference/api.md`, every edit or none as `add feature`
does, then runs `openapi`, which builds the API, writes the reference, the .NET client where the
project holds one and the web schema from it. `--dry-run` prints them
and writes nothing. The route follows the controller: under a `[Route]` that holds the resource the
action is routed from the id, and under one routed at the API root the action reuses the resource path
of the controller's own actions that carry the id, its row then written in full; a root-routed
controller with no such action is refused. The contracts join the manifest, and the controller's recorded hash follows the
action when the controller was untouched.
<!--#endif-->
<!--#if (!api)-->
The command prints the message to send, with its inputs, and the hosts that may offer it: send it
through `ISender` where one of them serves the feature.
<!--#endif-->

It refuses, writing nothing, a feature that does not exist, a feature of the simple shape, whose
operations are methods of its service, a use case the feature already has, an input that is not
`Name:Type` or whose type does not read as C#, a name given twice, and `id` where the feature's
repositories do not name one aggregate.

## Add a domain event

A feature of the rich shape announces a fact as a domain event, which other features react to
([ADR 0006](../decisions/0006-events-after-commit-without-an-outbox.md)):

```bash
dotnet cleanarch add event <Features> <Event> --consumer <Features>:<Reaction> [--fields "<Name>:<Type>, ..."]
dotnet cleanarch add event <Features> <Event> --unconsumed "<reason>"
```

The event's name ends in `DomainEvent`. `--fields` lists what it carries besides `OccurredOn`, which
it always ends with, in the grammar and with the name rules of [the fields](#fields) of a feature,
each of a type from `Guid`, `string`, `int`, `long`, `bool`, `decimal`, `DateOnly` and
`DateTimeOffset`, and with no length, precision, bound or `?`: no column holds an event, and the
aggregate checks a value before it raises the event with it. Left out, the event carries the id of
the feature's aggregate when its repository is the only one. An event is consumed or listed as unconsumed with its reason, so one
of `--consumer` and `--unconsumed` is required, never both.

The command writes, or nothing: each file is computed in memory and each anchor checked before the
first write, a missing anchor names the edit and stops with a non-zero exit, and an event that
exists is refused, so running it twice duplicates nothing. `--dry-run` prints what it would write.

- the event, a sealed record under `Model/Events/` of the feature's domain folder, with its test;
- with `--consumer <Features>:<Reaction>`, `<Reaction>On<Event>Consumer` in `Services/Consumers/` of
  the feature that reacts, its `AddDomainEventConsumer` line in that feature's `Add<Features>`, and
  a test that the consumer is registered for the event;
- with `--unconsumed`, the event's name and the reason in `_deliberatelyUnconsumed` of
  `DomainEventTests`.

What stays yours: the aggregate method that raises the event with `RaiseDomainEvent`, and its test;
the body of the consumer, which throws `NotImplementedException` until written and, as the ADR asks,
re-derives its precondition so running twice is running once. The dispatcher logs a consumer that
throws and drops the event, so the consumer's generated test `TheReaction_IsWritten` fails until you
replace it with the tests of the reaction: `verify` and the CI stay red until then, so a reaction
nobody wrote cannot pass the gate; and, when a consumer later joins an
event listed as unconsumed, the deletion of its entry, which `DomainEventTests` demands. Where the
project holds no `Add<Features>` in its `ApplicationModule`, register the consumer by hand with
`AddDomainEventConsumer`.
[Reaching another feature](reaching-another-feature.md#a-domain-event) covers raising the event,
writing the consumer, testing both, and what happens when a consumer fails.

## The closed lists a feature joins

Some lists change only on purpose, and a test fails until they name what you added:

| You added | What has to say so |
|---|---|
| <!--#if (anyFeature)--> |
| A registration | `HostComposition` in the architecture tests and the call in `ApplicationModuleTests` |
| <!--#endif--> |
| <!--#if (!anyFeature)--> |
| A registration | `HostComposition` in the architecture tests |
| <!--#endif--> |
| <!--#if (api)--> |
| A route or a contract | the OpenAPI reference, rewritten by `dotnet cleanarch openapi` and read in the review; the route's row under `## Routes` of [the API reference](../reference/api.md#routes), checked both ways by `ApiRouteTableTests` |
| An error code | its row and status in [the API reference](../reference/api.md#error-codes), checked both ways by `ErrorCodeTableTests` and against the status by `ErrorCodeStatusTests` |
| <!--#endif--> |
| An `IEntityTypeConfiguration` | `AppDbContext`, checked both ways by `PersistenceModelTests` |
| A new folder word under `Common/` or `Features/<F>/` | the vocabularies in `LayoutConventionTests` |
| A domain event | a consumer, or its name and reason in `DomainEventTests._deliberatelyUnconsumed`; `add event` writes either |
| A public interface under `Common/` that is not a port | `ApplicationPorts.NotPorts`, with its reason |
| <!--#if (api)--> |
| A use case | a host or a job that sends its message, checked by `MessageRoutingTests` |
| <!--#endif--> |
| An anonymous endpoint | `_anonymousActions` in `HttpSurfaceTests` |
| <!--#if (localAuth)--> |
| A folder of mail templates | `_templateFolders` in `EmailTemplateCoverageTests`, and its mails in `TheWalk_FindsEveryMailThisTemplateSends` |
| <!--#endif--> |
| A setting | its section and rows in [the configuration reference](../reference/configuration.md), checked by `ConfigurationSurfaceTests`; `add setting` writes them, and [adding a setting](adding-a-setting.md) gives the steps |
| A background service | the floor in `BackgroundWorkTests`; an instrument in `ObservabilityRegistrationTests` |
| A new project | `AppTemplate.slnx`, a `COPY` line in each host's `Dockerfile`, a mirror under `tests/`, its vocabularies in `LayoutConventionTests`, the module lists of `ModuleDependencyTests`; [adding a project](adding-a-project.md) gives the steps |
| Anything, eventually | `coverage.minimum`, re-measured as [testing](testing.md#coverage) says |

## Document it

1. Replace each prompt line of the feature page with what the feature does: what a caller gets, the
   rules, the routes, the settings, the tests; and the `description` of its frontmatter with the
   question the page answers, in one sentence.
2. Run `dotnet cleanarch docs-index`, so that the features index shows the description you
   wrote.
3. A choice another team could have made differently is an ADR: `docs/decisions/`, numbered from 1000,
   linked from the page's `## Decisions`.
<!--#if (anyFeature)-->

## Patterns from the examples

| When the feature | Read |
|---|---|
| <!--#if (todoLists)--> |
| owns child entities and protects a state | TodoLists, in every layer |
| is a flat aggregate reached from a timer | Reminders, and its job under the API's `Features/Reminders/Jobs` |
| <!--#endif--> |
| <!--#if (files)--> |
| keeps part of its state outside the database | Files, whose bytes live behind a storage port |
| <!--#endif--> |
| <!--#if (notes)--> |
| has no state to protect | Notes, a service over a store: the shape `--model record` writes |
<!--#endif-->
<!--#endif-->

## Promote a simple feature

<!--#if (api)-->
A record feature that gains a rule moves to the rich shape; the routes, contracts, `ETag` and owner
scoping do not change:
<!--#endif-->
<!--#if (!api)-->
A record feature that gains a rule moves to the rich shape; the version and the owner scoping do not
change:
<!--#endif-->

1. Extract the aggregate, its factory and rehydration, and its repository port.
2. Replace the store by the repository: keep the record, add the mapper and the tracker; the queries
   port stays.
3. Split the service into one use case per operation: a message, its handler and its validator.
<!--#if (api)-->
4. Point the controller at the use cases: it sends their messages through `ISender`.
<!--#endif-->
<!--#if (!api)-->
4. Point each host that called the service at the use cases: it sends their messages through
   `ISender`.
<!--#endif-->

The migration is empty unless the rule needs a column or an index.
<!--#if (anyWeb)-->

## Web pages for it

`add feature` and `add use-case` run `openapi`, which runs `pnpm api <reference>`, once the API serves the routes; run it
yourself after a route you change by hand. The generated client then holds their types, and an app
calls them through it. [The web front end](../features/web-frontend.md#calling-the-api)
shows the call.
<!--#endif-->

## Undo

`dotnet cleanarch remove feature <Features>` reports what removing it involves;
[removing features](removing-features.md#remove-a-generated-feature) says what `--apply` removes and
when it refuses.
