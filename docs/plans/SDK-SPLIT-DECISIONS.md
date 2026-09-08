# Decisions, and what was rejected

Companion to `docs/plans/SDK-SPLIT-PLAN.md`. This document exists so the analysis behind the split
is never redone. Each entry states the decision, then the reason, then what was rejected and why —
the rejections are the load-bearing half.

## 1. Five projects, one per SDK concern

**Decided.** `AppTemplate.Domain.Core`, `AppTemplate.Application.Core`,
`AppTemplate.Application.Auth`, `AppTemplate.Presentation.Core`, `AppTemplate.Api.Core`, plus five
mirrors under `Tests/`.

**Rejected — one Core per layer, three projects.** The presentation layer cannot be served by one
project without forcing every future non-HTTP host to reference the ASP.NET framework in order to
get a clock, a culture and an outbound HTTP policy. Two projects there keep that door open, which
is the stated goal: a desktop or CLI host calling the application layer directly.

**Rejected — an `AppTemplate.Api.Auth` project for the Auth controllers.** The presentation layer
is the least vertical layer of the four; extracting controllers buys a project and costs a
cross-assembly `<see cref>`, an application-part discovery question, and an ordering constraint
against the per-version OpenAPI loop that stays invisible until someone adds a v2. The Auth
controllers stay in `AppTemplate.Api`.

## 2. Names in `.Core`, and the composition-file convention is amended

**Decided.** The dotted form is kept, and `LayoutConventionTests.ModuleFileName` learns a second
form: the project name with the product prefix stripped and the dots removed. Composition files
become `ApplicationCoreModule.cs`, `PresentationCoreModule.cs`, `ApiCoreModule.cs`,
`ApplicationAuthModule.cs`. The six existing projects keep their names and their files.

**Reason.** The rule as written asks for the last segment plus `Module`, which makes three projects
demand a `CoreModule.cs` holding a class named `CoreModule` — and the rule's own stated purpose is
that `AddPersistenceModule` be findable from its call site. Nobody finds `AddApplicationCore` in
`CoreModule`.

**Rejected — concatenated project names** (`AppTemplate.ApplicationCore`). Leaves the rule
untouched, but loses the reading "the Core of that layer" and the grouping in the solution.

**Rejected — one uniform rule for every project.** It would rename five infrastructure composition
files and every citation of them in the docs, for no gain.

## 3. Namespaces follow the project name

**Decided.** A pure prefix substitution: `AppTemplate.Application.Core.Common` becomes
`AppTemplate.Application.Core.Common`, and so on, roughly 1000 occurrences swept in one pass.

**Reason.** It keeps the repository's cardinal rule — namespace equals project name plus folders,
no exception, checked over some 600 files — completely intact, and that rule then acts as the oracle
for the sweep itself.

**Rejected — keeping the namespaces via `RootNamespace`.** Zero `using` edits, but assembly and
namespace diverge, the cardinal rule has to be relaxed, and the NetArchTest rules stop being able
to tell two assemblies apart.

**Hazard to respect.** Every substitution is anchored on the full `AppTemplate.Application.` prefix.
Infrastructure namespaces share the shorter segments — `…Persistence.Common.Idempotency`,
`…InMemory.Features.Auth` — and would be corrupted silently.

## 4. The SDK projects keep `Common/` and `Features/`

**Decided.** Each new project has the same shape as every layer: `Common/<Responsibility>/` and
`Features/<Feature>/<Responsibility>/`.

**Reason.** `CONTRIBUTING.md` states it for all four layers, and respecting it means the rule about
what a project root may hold, and the rule about namespaces, both stay untouched. It also makes the
sweep a prefix substitution rather than a per-folder remap.

**Consequence accepted.** `AppTemplate.Application.Core.Common.Results` carries a word that no
longer sorts anything, and `AppTemplate.Application.Auth.Features.Auth` says Auth twice. The price
of an unbroken convention.

**Rejected — responsibility folders at the project root.** Shorter namespaces, but it forces an
amendment to the project-root rule and diverges from the shape every other project has.

## 5. `IAuditActor` does not move

**Decided.** The port stays in `AppTemplate.Infrastructure.Persistence`, and both adapters stay in
their hosts — `CurrentUserAuditActor` in `AppTemplate.Api`, `BackgroundAuditActor` in
`AppTemplate.Worker`.

**Reason, and it is executable.** `PortConventionTests.EveryApplicationPort_HasAConsumerInTheApplicationLayer`
fails with: *"A port the application layer declares but never consumes is a decision that has moved
out of this layer. Either something here should be calling it, or it does not belong to this layer
at all."* `IAuditActor` has exactly one consumer, the auditing interceptor, and it lives in the
persistence module. Moving the port to `Application.Core` would require an exemption in that rule —
the kind of shortcut this whole exercise refuses. On the merits Clean Architecture does not ask for
the move either: a port belongs to whoever needs it, the need is in infrastructure, the
implementation is in presentation, and presentation to infrastructure is the permitted direction.
A second reason arrived later and is just as decisive: moving it would drag a concrete
infrastructure module into **two** SDK projects.

**Rejected — a "nobody" default supplied by the persistence module.** It would delete
`BackgroundAuditActor` and two lines of the Worker's `Program.cs`, but today a host that forgets its
audit actor fails at composition; with a default it would stamp `null` into the database in silence.

**Cost accepted.** A derived `Program.cs` keeps two registration lines instead of none.

## 6. `SearchTerm.Create` becomes public

**Decided.** Public factory, private constructor, validation unchanged. `Cursor.Decode`,
`CursorKeys` and `SortTerm.Of` stay internal.

**Reason.** Two feature filters call it, so separating `Common/` from the business project would
break them with CS0122. The documented invariant — nothing outside may hold an instance that skipped
validation — is enforced by the factory itself, which returns a `Result`, not by its visibility. For
the other three, the only consumers are inside the project, so nothing changes.

**Rejected — an `InternalsVisibleTo` from `Application.Core` to the business project.** It would
hand the internals to the very assembly the restriction is aimed at, and no package would ever do
that.

## 7. Optionality is proved, not asserted

**Decided.** Assembly scanning gives way to opt-in registration per feature, and the rule that
asserts the container refuses to build when a module is missing is replaced by the test that is
missing today: the container builds *without* Auth.

**Reason.** `AddApplicationLayer` scans its own assembly, so every port the layer declares must
resolve in every host — which is why the Worker composes modules it does not use. That is precisely
what makes a module non-optional, and there is an existing test asserting it as an invariant.

**Limit stated rather than hidden.** The reminder feature is not composable without the to-do list
feature: its scheduling use case reaches into the other's read port, and that is the only ownership
check before a reminder is created. The example features are not four symmetric modules.

**Limit stated rather than hidden, second one.** Removing Auth is not deleting two projects. The
email module will not compile without it — its reminder notifier resolves a user profile to find an
address. `AppDbContext` *inherits* `IdentityDbContext`. The identity seeder needs a `UserManager`
that only the identity module registers, so the persistence/identity pair is bidirectional. And the
initial migration mixes nine identity tables with the single non-Auth table, which belongs to the
Core. A clean removal means regenerating the initial migration.

**Corrected while implementing it: the test named above cannot be written, because the limit just
stated forbids it.** "The container builds *without* Auth" is false of this template, and the two
paragraphs above are the reason — the persistence module registers `IIdentitySeeder`, which needs a
`UserManager` only the identity module supplies, so no composition that drops authentication gets
as far as a built container. The reminder feature is held the same way through `IReminderNotifier`,
whose one adapter is in the email module.

So the claim is split in two, and both halves are asserted:

- `LayerDependencyTests.TheApplicationLayer_KnowsNothingOfAuthentication` — read off the assembly
  manifests, neither the business project nor the mechanisms name anything in
  `AppTemplate.Application.Auth`. This is the half per-feature registration actually bought, and it
  is the half that matters for a derived project writing its own features.
- `ContainerCompositionTests.RemovingAuthentication_IsHeldUpByTwoInfrastructureCouplings_NotByTheApplicationLayer`
  — the composition that drops authentication is built, refuses, and the refusal is required to name
  `IIdentitySeeder` and `IReminderNotifier`. The caveat becomes executable instead of prose, and the
  day either coupling goes away the test says so rather than staying quietly true.

The lesson worth keeping: a decision document that promises an assertion should be checked against
its own limits section before the assertion is written, not after.

## 8. `Presentation.Core` is small; `Api.Core` takes nearly all of `Api/Common`

**Decided.** `Presentation.Core` holds five subjects: outbound HTTP, localisation, telemetry, the
"no caller" identity, and `PeriodicJob`. Everything else in `Api/Common` goes to `Api.Core`, options
and validators staying with their extension method.

**Reason, and it corrects an earlier reading.** The criterion "does this file depend on ASP.NET"
looked right and is wrong. It classifies about nineteen files as host-agnostic, but they are almost
all `*Options` types with an `internal` validator, and cutting them away from their `AddApiXxx()`
turns zero promotions into ten. Those options also describe purely HTTP concepts — `If-Match`,
`Cache-Control`, `X-Forwarded-For`, `Content-Security-Policy` — which no desktop host has any use
for. The right criterion is whether the *subject* is reusable by another host.

**Also decided.** `ShutdownHealthCheck` goes to `Api.Core`: it drains inbound requests, the Worker
has no readiness endpoint, and putting it in the shared project would hand the Worker the
health-check abstractions it does not have today.

## 9. The telemetry seam is parameterised, because the alternative is a cycle

**Decided.** The shared observability extension takes the diagnostics names from the host, and the
telemetry options take an explicit service name.

**Reason.** The Worker's observability extension names the three `*Instruments` classes that live in
its feature folders. Those folders stay in the host, so a shared project naming them would have to
reference the host that references it — a project-reference cycle that no visibility change can
resolve. The service name matters for a second reason: derived from the shared assembly, both hosts
would announce themselves under the same name in telemetry.

## 10. `Npgsql.OpenTelemetry` stays in the hosts

**Decided.** The shared tracer exposes an `Action<TracerProviderBuilder>` extension point, and each
host keeps its own `.AddNpgsql()` and its own package reference.

**Reason.** Both hosts call it, so it looks like shared code, but its nuspec pulls the full
PostgreSQL driver. A future Avalonia or CLI host would inherit a database driver from a
presentation SDK. Two duplicated lines are cheaper than that.

## 11. Package-grade, with four guardrails

**Decided.** The five SDK projects are written as if they were packages: the project-graph rule (an
SDK project references only SDK projects), `IsPackable` plus `dotnet pack` in CI, a frozen public
surface through `PublicAPI.txt` files, and `CS1591` re-enabled inside the SDK so every public member
is documented. `AppTemplate.Application.Auth` is held to the same standard as the rest.

**No version.** They are vendored: copied into each generated project, where they are meant to be
tuned. A version would promise a lineage that does not exist.

**What makes this possible.** `.editorconfig` neutralises CA1515 ("consider making this type
internal") with the justification that these types are the template's public surface. Without that
line every deliberate `public` would be a build error.

**Measured cost of the surface itself.** Two promotions are unavoidable — the outbound HTTP
extension and its method, which *are* the composition. Eleven others are avoided by moving the DI
registration into a public extension of the project that owns the type, which is what a package does
anyway. Analyzer cost: three `CA1062` on the direct route, zero on the composition route.

## 12. `Result` stays in `Application.Core`

**Decided.** Not in `Domain.Core`, even though the storage module would then reference the Core for
that type alone.

**Reason.** The domain of this template raises exceptions and never returns a `Result`. Moving the
type into the innermost layer would invite the domain to change style, which is a design decision
disguised as a file move.

## 13. `DevelopmentDatabaseExtensions` stays in the host

**Decided.** It does not move into `Api.Core`.

**Reason.** It names `AppDbContext` and the identity seeder in two `<see cref>` tags, so moving it
without an EF Core reference is two guaranteed CS1574 errors — and with one, `Api.Core` inherits EF
Core. It also decides a deployment policy (who applies migrations) that its own comment describes as
specific to this template.

## 14. The example features stay in the template

**Decided.** `TodoLists`, `Reminders` and `Files` are kept. The split moves no feature out of the
template, and `docs/REMOVING-THE-EXAMPLE-FEATURES.md` remains the procedure a derived project
follows if it wants them gone.

**Reason, beyond teaching.** Several fitness rules use them as their sensitivity probe — the rule
that forbids a persistence mechanism from naming a feature proves it bites by being applied to the
example mappers — and the claim that one use case answers both an HTTP request and a background loop
rests on the reminder and file loops in the Worker. Removing them from the template would leave
those rules green over nothing. `Files` is also a working capability rather than an illustration:
object storage with content inspection.

**What the split changes for them, and it is a gain.** The boundary between SDK and example becomes
a project boundary: afterwards the business projects and the two hosts hold little else. With opt-in
registration per feature, removing one becomes deleting a folder and one call, instead of editing a
module that scans the whole assembly. The removal document gets shorter, which is a wave 7 edit.

**Two limits unchanged.** `Reminders` is not independent of `TodoLists`, and removing `Auth` is a
far heavier operation than removing an example — both are stated above.

## 15. An SDK project is recognised by `IsPackable`, not by a list of names

**Decided.** The two package-grade rules of decision 11 take their subjects from the project graph:
a project is an SDK project when its `.csproj` declares `IsPackable` true. `ProjectReferenceGraph`
reads it alongside the reference list, and exposes `SdkProjects` and `IsSdkProject`.

**Reason.** The set then cannot drift from what `dotnet pack` actually produces, because it *is*
what `dotnet pack` reads. It also makes the metadata rule the guard of the boundary rule: a project
that opts into packaging inherits both checks in the same edit, and there is no second list to
remember — the failure mode `TemplatePackagingTests` documents for the guid list, one file over.

**Rejected — a hard-coded list of the five names.** It is a list nothing prompts anyone to update,
which is the defect this repository has already been bitten by twice, and it would let a sixth SDK
project be added with neither rule applying to it.

**Consequence, and it is handled.** Today no project under `Src/` declares `IsPackable`, so both
rules have an empty population. They report themselves **skipped** rather than passing, the way
`TemplatePackagingTests` does where the manifest does not exist: a rule that quietly succeeds over
nothing is what this suite exists to prevent, and a skip is visible in the run summary where a pass
is not.

## 16. `AppTemplate.Application.Core` has no umbrella `AddApplicationCore()`

**Decided.** `ApplicationCoreModule` exposes the three registration helpers — `AddUseCasesFrom`,
`AddUseCases`, `AddDomainEventConsumer` — and `AddPurgeExpiredIdempotencyKeys()`, and nothing else.
The single entry point named in `docs/plans/SDK-SPLIT-TARGET.md` is dropped.

**Reason, found while implementing it.** There is nothing for it to register. The project declares
ports, results, markers and validating factories — things a host implements or a caller names, not
things a container resolves. `Common/` holds no validator, `CurrentLanguage` is static, and an
`ICollectionPolicy` is reached through a static instance. So the method would have had an empty body
and a doc comment explaining that it does nothing, which is worse than its absence: a call that does
nothing reads, at every derived project's composition root, like the seam that makes the project
work.

**Rejected — keeping it as a stable seam** for the day the project gains something registrable
(wave 6 adds a cache port and an email templating surface). The argument is real and it lost to a
simpler one: adding the call then is a one-line edit to a composition root, made at a moment when it
means something, whereas shipping it empty asks every reader between now and then to believe it
matters.

**One correction to the reason TARGET gives for keeping the purge separate.** It says a host that
never schedules the purge has no reason to supply the two ports it resolves. True, but not the
operative fact here: *both* hosts schedule it — the API through `MaintenanceController`, the Worker
through its maintenance loop — so both call it. What makes the call mandatory is that
`AddApplicationLayer` scans its own assembly only, so the use case is no longer discovered at all.
The opt-in shape still earns its place for a derived project that drops idempotency.

## 17. There are two `Common/` folders per layer, and only one of them moves inward

**Decided.** A layer keeps two distinct shared halves, and they are not the same folder:

- `<Layer>.Core/Common/` — **agnostic of the business.** `Result`, `Error`, `PageRequest`,
  `SortOrder`, `IUnitOfWork`, `ICurrentUser`, `AggregateRoot<TId>`, `IDomainEvent`. Nothing in it
  names a feature, and nothing in it ever may: that is the entire claim these projects make, and
  what lets them be vendored into a project whose features nobody here has seen.
- `<Layer>/Common/` — the **business**-shared half. What several features share *as business*: a
  value object three of them spend, a DTO two of them answer with, a rule that spans them. It exists
  for the same reason any shared code does — so business logic is written once — and it stays in the
  business project.

**The sorting test, and it is one question.** Does it know a feature? If it names one, or would have
to the moment a second feature used it, it belongs in the business project's `Common/`. If it does
not and never will, it belongs one project inwards. `IDomainEventConsumer` and `DomainGuard` are the
worked example: both needed a domain type, and both needed only the primitives, so both went inward.

**Why this is written down.** `AppTemplate.Domain/Common/` and `AppTemplate.Application/Common/` are
empty, and the reason is a fact about this template's example features rather than a rule: everything
that was in them turned out to be agnostic. An implementation that reads that emptiness as a
prohibition pushes the next shared business concept into a Core project, where it is a feature
leaking into the SDK — the one defect these projects exist to prevent, arriving through the door
marked "there is nowhere else to put it".

**How it is enforced.** `LayoutConventionTests`' two vocabulary dictionaries accept a `null` entry
meaning "no such folder today", and check the claim in both directions: a folder appearing where the
entry says none fails, and a folder vanishing from under a list of words fails too. The failure
message for the first case names the choice rather than demanding words — decide which of the two
kinds it is, then list them. So creating either folder is a decision a reviewer sees, which is what
the closed vocabularies are for everywhere else.

## 18. The shared telemetry registration takes the host's assembly, and localisation binds only

**Decided.** Three seams in `AppTemplate.Presentation.Core`, each forced by something the shared
project cannot know.

**`AddObservability` takes an `Assembly`, not a service name.** `service.name` and `service.version`
are read from it. A string parameter would have worked for the name and left the version stale, and
reading this project's own assembly — which is what each per-host copy did, correctly, when it *was*
the host's assembly — would make every host announce itself as `AppTemplate.Presentation.Core`. Two
processes reporting as one is worse than either reporting nothing. Passing the assembly keeps the
property the per-host versions had, that a rename or a version bump cannot leave a stale literal,
and points it at the right assembly. `Tests` asserts it by composing twice with two different
assemblies and reading `service.name` off the built resource, so an implementation that read its own
would fail.

**The host's diagnostics arrive through `Action<TracerProviderBuilder>` and
`Action<MeterProviderBuilder>`.** A host's `ActivitySource` and `Meter` names are constants on
classes in its own feature folders; a shared project naming them would reference the host that
references it. The same seam carries `AddNpgsql()`, for decision 10's reason, and the ASP.NET
instrumentation, which cannot be here at all.

**`AddLocalizationOptions()` binds and validates, and does nothing else.** Reading the bound value
into `CurrentLanguage.Default` stays with each host, because *when* that happens is a host's own
question: one with a request pipeline does it as the pipeline is built, one with only background
work once the container exists and before the first loop runs. A shared helper doing it would pick
for both, and the wrong choice is silent — a worker whose loops reverted to English.

**Rejected — merging the two hosts' observability extensions outright.** It looks like the same
duplication as the outbound policy and is not: the API's half installs ASP.NET Core instrumentation,
which needs the framework reference this project exists without. Each host keeps a thin extension of
its own over the shared one, which is also where its own instruments go.

## 19. The `.Core` projects keep `Common/`, and the question is closed

**Decided.** Decision 4 stands, for all five `.Core` projects. Responsibility folders stay under
`Common/`, and no `.Core` project moves them to its root.

**Reason, and it is not the one decision 4 gave.** Decision 4 kept the folder to leave the
project-root and namespace rules untouched, and priced the empty segment as the cost. The segment
turns out not to be empty: it does not sort folders *inside* the project, but it names *which half
of the layer the project is*, which is exactly what decision 17 wrote down. Read the pair —
`AppTemplate.Application.Core.Common.Results` against `AppTemplate.Application.Common.<X>` — and the
parallel between the agnostic half and the business half is carried by the two segments together.
Dropping it from one side leaves the parallel invisible on both.

**What the reopened trade actually costs.** Two projects were priced; there are five, and three of
them — `Domain.Core`, `Presentation.Core`, `Api.Core` — have no `Features/` at all. Only
`Application.Core` still sorts anything with the word. So the alternative treats four projects one
way and one another, or accepts that at the root of `Application.Core` a reader meets ten
responsibility folders and a `Features/` that reads as the eleventh.

**Rejected — responsibility folders at the project root (shape B).** A rename of roughly a thousand
occurrences, plus `CONTRIBUTING.md`'s canonical tree, two vocabulary dictionaries, three rule failure
messages, and a layout rule that stops being one shape for fourteen projects, bought for seven
characters that mean something.

**Rejected — mixed per project (shape C).** It acts the asymmetry rather than resolving it, and makes
the layout rule answer "it depends on the project" to the only question it exists to answer.

**Consequence, and it is the useful one.** `Api.Core` is built in its final shape in wave 5, so the
thousand-occurrence rename never happens and the audit pass keeps only its content questions.

## 20. API versioning moves into `Api.Core`; the host keeps the attribute's package

**Decided.** `Asp.Versioning.Mvc` and `Asp.Versioning.Mvc.ApiExplorer` are referenced by
`AppTemplate.Api.Core`, which exposes `AddCoreApiVersioning()` and
`AddCoreOpenApiPerVersion(configure)`. `AppTemplate.Api` keeps its own reference to
`Asp.Versioning.Mvc`.

**Reason.** The six controllers each carry `[Asp.Versioning.ApiVersion("1.0")]`, so the host names a
type from that package and goes on naming it after the move. Each project declares what it names,
which is the explicit style this repository already holds. `Asp.Versioning.Mvc.ApiExplorer` leaves the
host, because once the per-version loop moves the host names nothing from it. Both packages carry the
ASP.NET framework reference in their own nuspec, which is legal in this project and nowhere else in
the repository.

**Reason for moving the subject at all.** The per-version block is a throwaway `ServiceProvider`, a
`#pragma warning disable ASP0000` with a paragraph justifying it, and a loop whose `ShouldInclude`
is what keeps a v2 out of the v1 document. It is the most subtle passage in `Program.cs` and the one
a derived project is most likely to get wrong, which is the definition of what belongs in the SDK.

**Rejected — leaving versioning in the host.** It leaves that block to be re-derived by every
derived project.

**Rejected — a transitive reference for the host.** `Asp.Versioning.Mvc` would arrive without being
declared by the project that names the attribute.

**Amends `docs/plans/SDK-SPLIT-TARGET.md`**, which states both that `Api.Core` exposes
`AddCoreApiVersioning()` and that the host keeps these two packages. The first cannot compile without
them.

## 21. The bearer scheme's description names no route

**Decided.** `OpenApiSecurityTransformer` describes the scheme without naming an endpoint. It stays
`internal`, registered by this project's own composition.

**Reason.** The Auth controllers stay in the host (decision 1), so a login route written into an SDK
document transformer is a feature address inside the SDK. The sentence also buys a reader little: the
login operation is in the same document, with its own description.

**Rejected — an options type, or a parameter, carrying the description.** The seam already exists.
`AddCoreOpenApiPerVersion(configure)` lets a host add its own `IOpenApiDocumentTransformer`, so a host
that wants to name its own login route does it there, at the cost of nothing added to a frozen public
surface.

## 22. `Api.Core` maps the health endpoints, and not from inside `UseCorePipeline()`

**Decided.** `MapCoreHealthEndpoints()` is public, called by the host beside `MapControllers()`.
`AddCoreHealthChecks()` still returns the builder so the host chains its own `DbContext` check.

**Reason.** The two probes carry four couplings, none of them visible from the call site: the
`"ready"` tag shared between `AddCoreHealthChecks()` and the host's own check, `AllowAnonymous()`
against a default-deny fallback policy, `DisableRateLimiting()` — whose comment explains the cascade
it prevents — and the `/health` prefix the tracer filter and the request log both exclude. A derived
project recomposing that by hand loses one. And `AddCoreHealthChecks()` returning a builder only
means something if something later maps the endpoints; otherwise the host does half the subject.

**Why not inside the pipeline call.** Mapping an endpoint is not middleware, `UseCorePipeline()` runs
before `MapControllers()`, and the visible order in `Program.cs` is what that file is for. A named
call keeps the order readable.

**Consequence, and it is a gain.** The `/health` prefix becomes one public constant of this project,
read by the tracer filter, by the request-logging branch and by the mapping — three independent
literals today.
## 23. The API's observability splits; the host keeps a residue, and the rule learns to require it

**Decided.** The ASP.NET half of `Api/Common/Observability/` moves into `Api.Core` — the ASP.NET Core
instrumentation on traces and metrics, the `/health` filter, the rate-limiting meter, the request-log
middleware and its pipeline placement. `AddApiObservability` there takes the same two delegates
`Presentation.Core` already takes, and forwards them. The host keeps
`Api/Common/Observability/ObservabilityExtensions.cs` as a residue holding what only it can say:
`AddNpgsql()` and `AddMeter("Npgsql")`.

**Reason, and it is one line of the file.** `AddMeter("Microsoft.AspNetCore.RateLimiting")`.
`AddApiRateLimiting` moves to `Api.Core`, so leaving the meter name behind ships a mechanism with its
own diagnostic amputated: the rejection count the rate limiter surfaces nowhere else would be
exported only by a derived host that thought to type the name. Whoever owns the subject owns the name
of that subject's meter. `Npgsql` is not this project's subject — decision 10 — and stays in the host.

**What a derived host stops re-deriving.** The instrumentation filter, the enrichment that puts
`HttpContext.TraceIdentifier` on the span, the `/health` prefix shared between that filter and the
log branch, the middleware, and where it sits relative to the exception handler. The traceId-to-log
join the documentation sells as a guarantee is exactly the detail nobody reconstructs.

**Rejected — moving only the middleware.** It splits one file across a project boundary and forces
`RequestLoggingMiddleware` public, for a type no caller should name.

**Rejected — leaving the whole folder in the host.** It delivers none of the above.

**Rejected — no seam, with the host appending its instrumentation afterwards.** Strictly simpler, and
it silently loses a property: those calls append after the OTLP exporter, while the existing seam is
invoked before it on purpose, so that a host can still set a sampler or add a processor. `AddSource`
and `AddMeter` do not care; the next thing someone adds does. It also asks a reader to learn a second
registration idiom for the one the repository already has.

**The trap this decision must not walk into, and the rule now says so.**
`ObservabilityRegistrationTests` reads a host's `Common/Observability/*.cs` as its registration text,
and a missing folder yields an empty string. Dissolving the residue into `Program.cs` therefore makes
the *first instrument a derived host declares* an offence with no folder named to fix it in — and the
count assertion cannot catch it, because the floor of six is met by the Worker alone. So the rule
gains the assertion it lacked: a host that declares an instrument must have a non-empty
`Common/Observability/`, with a message that names the choice.

**Cost accepted.** Three storeys of delegate — host, `Api.Core`, `Presentation.Core`. Answering
"where does the tracer get its sources" costs three files. And in this repository today the new seam
has one caller carrying two lines.

## 24. Scalar leaves `Api.Core` entirely

**Decided.** `Api.Core` neither references `Scalar.AspNetCore` nor names it. `UseCorePipeline()` takes
an optional `ResponseContentSecurityPolicy` — a named public delegate — invoked from inside its own
`OnStarting` callback; returning `null` keeps the configured policy. The host keeps its
`MapScalarApiReference` call and gains
`Api/Common/Security/ApiReferenceContentSecurityPolicy.cs`, holding the `/scalar` prefix, the policy
directive by directive with its present prose, and the read of the nonce key.

**Reason.** The reference page's policy is an empirical observation about a versioned third-party
JavaScript bundle: each directive answers something that bundle does, the external font host
included, and Dependabot will move the version. That knowledge belongs beside the `PackageReference`
that pins it. A project whose stated claim is that it is ignorant of the application consuming it
cannot ship a security policy shaped for one vendor's UI, for a derived host that may have chosen
another or none.

**Reason, second.** The environment check leaves the SDK with it. The host branches on development
where it already branches for the OpenAPI document and for migrations, so the pipeline call passes
the policy only in that branch. One conditional fewer inside the SDK, not one more.

**Why the delegate hangs off `UseCorePipeline` and not off `UseApiSecurityHeaders`.** That method is
internal, absorbed into the monolith (decision 8, and decision 26 below). The pipeline entry point is
the only public caller, so the parameter is its.

**Why one writer of the header, not two.** `OnStarting` callbacks run in reverse registration order,
so two middlewares each writing the policy would make "which wins" a function of pipeline position.
`Api.Core` stays the single writer, fed by the host's delegate.

**Why a named delegate rather than a bare `Func`.** This surface is frozen and is read instead of the
code — which is why `CS1591` is re-enabled here. A `Func` of a context to a nullable string says
neither that a content-security policy is wanted nor that `null` means "keep the configured one".

**Rejected — keeping Scalar inside `Api.Core` behind two options.** It moves the `using` from one file
to another and calls it fixed, and hands the SDK a third-party documentation UI as a dependency.

**Rejected — the policy as a configuration string.** The nonce is per request, so a static
configuration value cannot carry it without an interpolation placeholder this repository has nowhere
else, plus the header-injection validation to extend over it. It also spreads Scalar's specifics into
every derived host's own configuration file.

**Rejected — the delegate as a property on `SecurityHeaderOptions`.** `ConfigurationSurfaceTests`
matches any public member with a getter, so a code delegate would read as a configuration key and
demand a documented row in the configuration guide for a knob no configuration file can set. The
extension point has to be a method parameter.

**Amends `docs/plans/SDK-SPLIT-TARGET.md`,** which lists `Scalar.AspNetCore` among this project's
packages. `SDK-SPLIT-PLAN.md` and the handoff both call this a leak to fix rather than move; that
reading wins.

**Cost accepted.** A derived host that mounts the reference page and forgets the delegate gets a blank
page. `ApiReferencePolicyTests` is what says so, in those words, and the template ships the file and
the call already wired.

## 25. A host's `Common/` is a third kind, and its criterion is executable

**Decided.** Decision 17's two kinds become three. Beside `<Layer>.Core/Common/` (agnostic of the
business) and `<Layer>/Common/` (shared as business), a host's `Common/` holds **what the host cannot
delegate**: what names a concrete infrastructure module, or what decides a deployment policy.

**Reason.** Decision 17 says two per layer, and after wave 5 the presentation layer visibly has
three: `Presentation.Core/Common/` and `Api.Core/Common/`, agnostic at two transport scopes, and
`Api/Common/`. The document did not describe that layer, and a `Common/` with no stated criterion is
the folder everything lands in.

**Why the criterion is worth writing rather than assuming.** Of the forty-eight files in
`Api/Common/`, exactly three import `AppTemplate.Infrastructure.*`, and they are exactly the three
that stay: `DevelopmentDatabaseExtensions` (decision 13), `CurrentUserAuditActor` (decision 5) and
`AuthorizationPolicies`. The boundary is read off the import graph rather than judged.

## 26. Public is exactly what a host names

**Decided.** In `Api.Core`, the `*Options` types are public — their section names are the contract
with whoever deploys — and so is anything a controller or a `Program.cs` names: `ApiControllerBase`,
`PagedResponse`, the two attributes, `MapCoreHealthEndpoints`, `AddApiCore`, `UseCorePipeline`,
`AddCoreApiVersioning`, `AddCoreOpenApiPerVersion`, `AddCoreHealthChecks`. The nine
`AddApiXxx`/`UseApiXxx` extensions, the option validators, the filter and the middleware are
`internal`.

**Reason.** `UseCorePipeline()` is monolithic because the pipeline's ordering constraints are coupled
pairwise (decision 8), so exposing its steps one by one offers a caller exactly the composition the
monolith exists to forbid. The `Add` halves have no ordering constraint, but a public `Add` with no
matching `Use` is a seam that cannot be used.

**Why a smaller surface is not a restriction here.** These projects are vendored, not published
(decision 11): a derived project copies them and tunes them. A host that wants the nine calls one by
one edits the file it owns, and the escape hatch costs nothing to leave closed by default.

**How it is settled per type, and it is not a judgement.** The compiler answers it: make everything
internal, build the host, and promote exactly what fails.

## 27. Withdrawn — the owned-aggregate guard does not populate the business `Common/`

**This entry claimed** that the load-and-check method duplicated in `TodoListService`,
`StoredFileService` and `ReminderService` should be factored into `AppTemplate.Application/Common/`
over an abstraction in `AppTemplate.Domain/Common/`, and that doing so would finally give the
template both halves of decision 17. **It is withdrawn**, and the reason it was wrong is worth more
than the entry was.

**It was written without reading the answer already in the code.** `IStoredFileService`'s own
summary states the counter-argument: the three are "the third instance of the pattern rather than a
generalisation of the first two", what they share is "a shape, not a rule", and it cites
`CONTRIBUTING.md`'s rule — extract what two real cases prove identical, and measure first.

**Measured, as that rule demands.** 42, 45 and 42 lines; `diff` between the two closest is nine
hunks, and **every one of them is a type substitution** — the aggregate, its repository, its
not-found error, the parameter name. Not one line of differing logic. So the existing comment
overstates its own case: the three do the same thing, and passing three arguments to a generic gate
is not "writing the gate again".

**And the entry still fails, on its own criterion.** A gate generic over an ownership abstraction and
`IVersioned` names no feature and never would. Decision 17's sorting test therefore sends it
*inward*, to `AppTemplate.Application.Core`, not sideways into the business project. What stays
business is the policy that a non-owner is answered exactly as an absent resource — and that policy
is expressed by each feature passing its own not-found error, which keeps it in the feature. So the
extraction, if it is made at all, populates the SDK's `Common/` and leaves both business `Common/`
folders exactly as empty as they are.

**What this establishes, and it is the useful part.** The business halves cannot be populated by
moving anything that exists. The domain has **no** cross-feature import at all; the application has
exactly two, and both must stay — the domain-event consumer because that is the decoupled mechanism
working, and `ScheduleReminderUseCase` because relocating it would relocate its dependency without
removing it. Decision 17's emptiness is not an accident waiting to be corrected: it is what this
template's example features actually are.

**So the open question is a different one, and it belongs to the repository owner.** A template whose
purpose is to show the canonical shape currently exhibits one of the two kinds of `Common/` and only
describes the other. Closing that gap means either inventing a shared business concept the example
features do not have, or documenting the shape without exemplifying it. Both are defensible and the
choice is not the implementer's.

## 28. The `AddOpenApi` call stays with the host, because the generator hooks its call site

**Decided.** `Api.Core` exposes `CoreApiVersionGroups()` — the throwaway `ServiceProvider` that reads
the version list, with the `ASP0000` suppression and the reasoning that rules it out — and
`UseCoreDocumentConventions(groupName)`, which declares the bearer scheme and restricts the document
to its own group. The four-line `foreach` that calls `AddOpenApi` stays in `Program.cs`.

**Reason, measured rather than argued.** The XML documentation that becomes a schema's
`description` is collected by the source generator in `Microsoft.AspNetCore.OpenApi`, and that
generator hooks the `AddOpenApi` call through an interceptor — so the comments it can see are the
ones in the assembly holding the call. With the loop inside `Api.Core`, the generated support file
in the host knew `ResetPasswordRequest` and `ConfirmEmailRequest`; the one in `Api.Core` knew
neither, and it was `Api.Core`'s interceptor that fired. Every request and response contract of the
host lost its description, and nothing failed to compile.

**How it was caught, and it is the reason that test exists.**
`OpenApiDocumentTests.ASchemaDescription_ResolvesTheTypeThatItsSeeCrefNames` reads one description
out of the served document and asserts it resolves the type its `<see cref>` names. It threw
`KeyNotFoundException`. The API host's `.csproj` already explains that descriptions are the thing
worth protecting here — that is why `Asp.Versioning.OpenApi` is refused — so the same assertion was
already standing guard over the same property from the other direction.

**Rejected — leaving the whole block in the host.** It would return the `ASP0000` suppression, the
throwaway provider and the paragraph justifying both to every derived project, which is what
decision 20 moved them out of.

**Rejected — registering the XML transformer from `Api.Core` explicitly.** It would need the host's
documentation file, which a project the host references cannot name.

**Amends `docs/plans/SDK-SPLIT-TARGET.md`,** which lists `AddCoreOpenApiPerVersion(configure)` in
this project's composition. That method cannot exist without losing the descriptions.

## 29. The host declares its own entry point

**Decided.** `Program.cs` ends with `public partial class Program;`, and the integration factory is a
`WebApplicationFactory<Program>`.

**Reason.** `WebApplicationFactory<TEntryPoint>` uses the type argument to find the assembly that
owns the entry point, and refuses one whose assembly has none. The factory used the API's base
controller, on the stated grounds that any public type of the host would do — true until that base
controller became part of a class library. Of the host's remaining public types, all are either
static, and so cannot be a type argument, or a feature's controller, which would tie the test host
to a feature.

**Why this rather than a grant.** Top-level statements compile into a class the compiler declares
`internal`, and the usual alternative is an `InternalsVisibleTo` for the integration project. This
repository grants internals to exactly one assembly per project — its own mirror — and a second
grant out of the host for a test-host detail is a worse trade than a type declaration that costs the
host no member. It also makes `TEntryPoint` name the entry point, which is what the parameter is
called.

## 30. The AV0029 suppression is about a generated file, not about controllers

**Corrected.** `docs/plans/SDK-SPLIT-TARGET.md` and the handoff both said the API host's
project-level `NoWarn` for `AV0029;AV0030` "targets controllers and must not be copied to
`Api.Core`". The host's own `.csproj` says otherwise, and it is right: the suppression sits at
project level *because* `AV0029` fires inside `OpenApiXmlCommentSupport.generated.cs`, which no
`#pragma` can reach.

**Consequence.** That generated file appears wherever `Microsoft.AspNetCore.OpenApi` is referenced,
which after this wave is both presentation projects. `Api.Core` carries the same suppression, with
the same justification and the same revisit condition — `OpenApiDocumentTests`, which asserts on
exactly the descriptions that would regress if the alternative package were adopted.

**The lesson, and it is the same one decision 7 recorded.** A plan document that states a
consequence should be checked against the code it describes before the consequence is acted on. Here
the plan read a suppression's target off its symbol name rather than off the comment above it.
## 31. The signed-grant expiry test proves validity with a control, not with a time window

**Decided.** `SignedGrantTests.AGrantWhoseLifetimeHasElapsed_IsRefused` serves the object once
through a grant of its own with a ten-minute lifetime, and only then loops on the short-lived grant
until the store refuses. The `refusedBeforeItsDeadline` check and the signature-granularity constant
it depended on are gone.

**Reason.** The old check read "a refusal arriving while the grant should still work means the grant
was never valid" — a sound claim about expiry, and an unsound one about anything else that answers
`Forbidden`. A store still warming up does, and this suite starts twelve test projects and four
containers at once. So the one assertion protecting the test from proving nothing was also the one
thing that could fail for a reason unrelated to its subject.

**What the control proves that the window did not.** The window could be satisfied with the object
never having been served at all: a first request landing after the lifetime had elapsed recorded
nothing, the loop ended on its own exit condition, and the test passed having observed no success.
The control asserts a success, which is strictly more, and it does so under a lifetime nowhere near
elapsing — so establishing validity no longer races the very window whose expiry is the subject.

**What is claimed, and what is not.** The failure did not reproduce on this machine, before or after
the change, over six full-suite runs. So this is not a repair of an observed failure; it is the
removal of the mechanism that made one possible, and the six runs are evidence of no regression
rather than of a fix. The subject under test — the grant lifetime and the store honouring it — is
untouched.

**Rejected — marking the test, or accepting an intermittently red gate.** A gate that is sometimes
red teaches everyone to re-run it, which costs more than the test is worth. Marking it would have
retired the only assertion this repository makes about a leaked URL ceasing to be worth anything.

**Rejected — lengthening the lifetime.** It widens the window without removing the ambiguity: a
transient `Forbidden` inside a longer window is still read as "never valid".

## 32. Tagging becomes the worked example of the business `Common/`

**Decided.** `Tag` moves out of `Features/TodoLists/ValueObjects/` into
`AppTemplate.Domain/Common/Tagging/`, joined by a new `TagSet` that states the three rules a tagged
thing obeys. `StoredFile` gains tags through the same set, and the application layer gets
`AppTemplate.Application/Common/Tagging/TagValidation`, which the four TodoLists validators and the
one new Files validator share. Both business `Common/` folders exist, and each holds something two
features use.

**Why this and not one of the extractions the search actually found.** Decision 27's withdrawal
established that nothing already duplicated could populate these folders: the owned-aggregate guard,
the paginated read, the request binder, the audit block — all measured, all real duplication, and
all agnostic, so all of them belong one project inwards. The template therefore exhibited one of
decision 17's two kinds of `Common/` and only described the other. Closing that meant promoting an
existing concept to a shared one, and tagging was the only candidate that was already mature: eleven
consumers, four duplicated validators, and a rule nobody had to invent.

**The test that puts it here rather than in `.Core`, and it is mechanical.** `TagSet` names `Tag`,
and a `.Core` project may name no business type — `PackageBoundaryTests` holds that over the project
graph. Its *generic* shape, a bounded de-duplicating collection, would have been agnostic; what it
actually is is not. The same argument one layer up: `TagValidation` names `Tag` and reads the
domain's constants rather than literals. This is the criterion that failed for the ownership guard,
which named no business type at all, and it is why that one went inward and this one did not.

**What the rule actually is, which is why it was worth extracting.** Three parts, none obvious:
a tag is held once however many times it is sent; the cap is checked only for a tag that is
genuinely new, so re-sending an existing tag stays a no-op on a full set; and a replacement is
total, with the removals applied before the additions, so the cap governs the result rather than the
union — swapping twenty tags for twenty others is not a request for forty. `TagSetTests` states all
three. Stated once, they are reachable by any aggregate that owns a set; stated inside one feature's
entities, they reach nothing else — which is the whole of the argument for the folder.

**The cap and the subject stay with the aggregate**, because how many tags a thing carries is a
decision about that thing. Both aggregates happen to choose twenty; inventing an asymmetry to
demonstrate the parameter would have been illustration rather than capability, which decision 14
holds the example features to.

**What it cost, honestly.** A migration (`StoredFileTags`, mirroring `TodoItemTags`), a tag record
and its configuration, reconciliation on both mapper directions, an `Include` on all four repository
paths that materialise a file, a `PUT .../tags` route with its request contract, and edits to two
closed counts and one closed property list. Three of those were found by tests rather than by
reading: the round-trip fidelity test caught the insert path having no tags, the write-fidelity
sensitivity test caught two samples that no longer differed, and the integration test caught the
missing `Include` — which no unit test could have, and which would have re-inserted an existing key
on the second write of any file.

**Rejected — a shared due date as well.** Also defensible, and deferred rather than refused: it
resorbs no existing duplication, it is purely additive, and it would put a second temporal concept
beside `Reminder.DueAt` in the same pass. Once `Common/` exists, adding it is one value object and
one consumer.

**Rejected — inventing a concept with no existing code behind it.** The whole point of the search
that preceded this was to avoid exactly that, and `CONTRIBUTING.md`'s rule is explicit about a
guessed abstraction being worse than an assumed duplication.

## Two corrections to `docs/plans/SDK-SPLIT-TARGET.md`, found while implementing wave 4

- It says the Worker "loses five package references" to this project. It loses **four**:
  `Microsoft.Extensions.Http.Resilience`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
  `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.Http`. The API loses the same
  four. What the Worker keeps is what TARGET already lists.
- It lists `Common/Jobs` and `PeriodicJob` under this project's content. Both are wave 6 — TARGET
  describes the finished state, and `PeriodicJob` does not exist yet, so this project has four
  `Common/` folders rather than five until recurring work gets its abstraction.

## Two pre-existing defects, to be fixed rather than reproduced

Found while measuring what a shared `PeriodicJob` could factor out. Neither is caused by the split.

- The maintenance loop **neither counts nor logs** a disabled purge, while the other two loops
  explicitly refuse that silence. A disabled purge is today indistinguishable from a dead loop.
- The maintenance and reminder loops do not run their shutdown log when the stop lands
  mid-iteration; the file loop guarantees it does.

## One inaccuracy to correct in passing

`AppTemplate.Infrastructure.Persistence.csproj` explains an absent package reference by NU1510.
The reasoning is sound — the transitive reference suffices — but the attribution is wrong: that
project carries no ASP.NET framework reference, and NU1510 cannot fire there. `Api.Core` will be the
first project in this repository where it genuinely can.
