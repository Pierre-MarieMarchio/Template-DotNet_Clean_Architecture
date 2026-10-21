# Testing

Each risk this project carries is held by one type of test, the cheapest that can fail for its real
cause. A second type is added only when it can fail for a cause the first cannot see
([ADR 0020](../decisions/0020-one-risk-one-type-of-test.md)). The stack is xUnit v3 on
Microsoft.Testing.Platform, Shouldly, NSubstitute, NetArchTest for the architecture rules and
Testcontainers for PostgreSQL.

## The test types

| Type | Proves here | Never used for | Lives in |
|---|---|---|---|
| Unit | a decision of one type: an invariant, a refusal with its control, a choice a mapper or a policy makes | wiring, a framework or compiler guarantee, an argument passed on, isolation between owners | `Tests/<Layer>/<Project>.UnitTests/Features/<Feature>/`, mirroring `Src/` |
| Architecture | a property of a population ("every use case…", "no domain type…") and the composition of the real host | the behaviour of one type | `Tests/Architecture/AppTemplate.Architecture.Tests` |
| Integration | what only the real stack decides: authentication, ownership in the query, races, the schema, the real stores | logic a unit test already decides branch by branch | `Tests/Integration/` |
| Contract | the surface a client relies on: routes, statuses, problem `code`s, the OpenAPI document, every response member on the wire | internals | `Tests/Integration/AppTemplate.Api.IntegrationTests/OpenApi/`, the error-code tests, one wire read per feature |
| End-to-end | the stack as shipped composes and works: the Compose file, the images, the migrations at start-up, the mail path, the object store, the web relay | anything a lower type can prove | `Tests/EndToEnd/AppTemplate.EndToEnd.Tests`, over HTTP against the Compose stack |
| Load smoke | nothing comes apart under concurrency | a gate; it never fails a build | `Tests/Load/smoke.js`, a k6 script against a running stack |

Unit and architecture tests need nothing and run in seconds. The integration and contract tests need
Docker and run in minutes. The end-to-end suite needs the whole stack built and started, and runs
apart from both.

## The risks this project holds

| Risk | Type | Held by |
|---|---|---|
| Domain invariants and rehydration | unit | the domain suites, one test per invariant and per refused row |
| A use case's decisions | unit | the application suites, one test per branch, each refusal with its control |
| Request and configuration validation | unit; architecture for "every section has a validator" | the validator tests, boundary rows only; `ConfigurationSurfaceTests` |
| Layers, folders, closed lists, composition | architecture | `Tests/Architecture/AppTemplate.Architecture.Tests`, `ContainerCompositionTests` among them |
| The HTTP contract | contract | `OpenApiDocumentTests` against the committed reference; `ErrorCodeTableTests` and `ErrorCodeStatusTests` against [the API reference](../reference/api.md#error-codes) |
<!--#if (accounts)-->
| Default deny | integration over every mapped endpoint; architecture for the anonymous list | `DefaultDenyAuthorizationTests`, `HttpSurfaceTests` |
<!--#endif-->
<!--#if (anyFeature)-->
| Data ownership, in the query ([ADR 0009](../decisions/0009-ownership-is-in-the-query.md)) | integration, every entry point that names a row | each feature's ownership tests in the integration suite |
<!--#endif-->
<!--#if (localAuth)-->
| Sign-in, sessions, two-factor, passkeys, lockout | integration; unit for each use case's refusals | the `Security/` and `Auth/` integration folders, `Tests/Integration/AppTemplate.Infrastructure.Auth.IntegrationTests` |
| Anonymous routes reveal nothing ([ADR 0014](../decisions/0014-anonymous-routes-reveal-nothing.md)) | integration | `LoginUniformityTests` and the "answers as a missing one" tests |
<!--#endif-->
| Optimistic concurrency | integration where two writers meet; unit for the precondition decision | each feature's concurrency tests, `ApiControllerBaseTests` |
| Idempotency | integration for the lease; architecture for the opt-in to `POST` | `IdempotencyLeaseTests`, `IdempotentActionTests` |
| Migrations and model drift | unit, and every integration run migrates a real database | `PendingModelChangesTests` |
| Background work under a lease | integration; architecture for the hosted services | `LeaderLeaseTests`, `BackgroundWorkTests` |
<!--#if (files)-->
| Doubles that tell the truth | contract tests run against the double and each adapter | `FileContentStoreContract`, `FileContentInventoryContract` |
| Stored-file safety: keys, grants, inspection | unit for the policy and the state machine; integration for the stores | the files suites ([ADR 0018](../decisions/0018-stored-files-are-keyed-granted-and-refused.md)) |
<!--#endif-->
| The stack as shipped | end-to-end, one test per flow | `Tests/EndToEnd/AppTemplate.EndToEnd.Tests` |

A feature that brings a risk of its own adds a row here.

## Where a new feature's tests go

- **Domain:** one test per invariant, one per refused rehydration.
- **Use case:** one test per decision branch, each refusal with the control that shows the same call
  succeeding.
- **Validators:** one test per rule, at its boundaries.
- **Integration:** ownership on every entry point that names a row; concurrency where two writers
  meet; one test that reads every member of every response the feature serves.
- **Contract:** the feature's codes as rows of [the API reference](../reference/api.md#error-codes),
  and the OpenAPI reference rewritten with `dotnet run Tools/Tasks.cs openapi` and read in the review.
- **End-to-end:** nothing, unless the feature changes what the stack runs: a new service, a mail, a
  relay.
- **Nothing** for composition, default deny or folder conventions: the tests over the whole surface
  pick the feature up.

## What not to test

- A property of a population as one row per member: write one test that finds the members and names
  each offender.
- A framework or compiler guarantee: a `ThrowIfNull`, nullability, the container resolving.
- A restated constant, modifier or signature.
- An argument forwarded to a substitute, a cancellation token included: CA2016 fails the build.
- Ownership against a substitute that returns whatever the test arranged.
- Anything written for the coverage figure.

## Doubles

Substitute a port with NSubstitute when the test only arranges an answer. Write a double by hand when
the test depends on behaviour a substitute would have to fake: a recording logger, an in-memory
store. A hand-written double that stands in for an adapter runs the contract tests of that adapter,
so the double cannot drift from what it replaces.
<!--#if (files)-->
The contract bases live under
`Tests/Infrastructure/AppTemplate.Infrastructure.Storage.UnitTests/Features/Files/Contracts/` and are
linked into the in-memory and integration suites.
<!--#endif-->

## Running them

```bash
dotnet run Tools/Tasks.cs test                                          # the solution
dotnet run Tools/Tasks.cs test --no-integration                         # the projects that need no Docker
dotnet test --project Tests/Architecture/AppTemplate.Architecture.Tests # the architecture rules alone
dotnet run Tools/Tasks.cs openapi                                       # rewrite the OpenAPI reference after a contract change
dotnet run Tools/Tasks.cs end-to-end                                    # the end-to-end suite against a stack of its own
```

Each integration project starts one PostgreSQL container and one host for the whole suite, and every
class joins one collection, so tests run one after another. `IntegrationTestBase` resets the rows, the
mailbox, the captured log and the clock before each test; `TestDatabase` truncates the schemas it
lists, and a schema it does not list leaks rows into the next test.
<!--#if (files)-->
The `Storage/` suite starts the object store the stack runs, at the same digest.
<!--#endif-->

`global.json` names Microsoft.Testing.Platform as the runner: each test project is a console
application, and the platform runs the modules the solution lists. A test project absent from
`AppTemplate.sln` never runs, so a new one needs `<IsTestProject>true</IsTestProject>` and an entry in
the solution; CI fails when one is missing.

A run killed outright leaves its containers behind, because the resource reaper is off
(`ResourceReaperSwitch`). Remove them by label:

```bash
docker rm -f $(docker ps -aq --filter label=org.testcontainers=true)
```

## The end-to-end suite

`dotnet run Tools/Tasks.cs end-to-end` builds and starts the Compose stack from `.env.example` under a
project name of its own, waits until every service is healthy, runs the suite, prints the stack's
logs on a failure, then removes the stack with its volumes. `--keep-stack` leaves it running. The
development stack keeps its data, but it holds the same ports: stop it first.

The suite reads the ports from the environment, then from `.env.example`, as Compose does, so a
variable set before the task moves the stack and the suite together.
<!--#if (anyWeb)-->
It finds every web app by its `WEB_<APP>_HOST_PORT` line.
<!--#endif-->
<!--#if (externalAuth)-->
The task names an unreachable placeholder provider when `EXTERNAL_AUTH_AUTHORITY` is unset: no test
signs in, and the API starts.
<!--#endif-->
<!--#if (files)-->
It sets the file inspection to run every ten seconds, so a deposit becomes readable within seconds.
<!--#endif-->

| Test class | Risk it holds |
|---|---|
| `StackTests` | the stack starts, migrates and reports ready; an unmapped route is refused with the API's problem code |
<!--#if (localAuth)-->
| `SessionTests` | the confirmation mail reaches Mailpit with a link that confirms the address; sign-in, `/auth/me`, refresh and sign-out work on the real stack, and the refresh token is refused after sign-out |
<!--#endif-->
<!--#if (localAuth && files)-->
| `FileTransferTests` | a deposit on the signed grant reaches the object store, the inspection pass runs, and the content comes back through its signed URL |
<!--#endif-->
<!--#if (anyWeb)-->
| `WebAppTests` | each web app serves its page and relays `/api` to the API |
<!--#endif-->

The project sets `IsTestingPlatformApplication` to `false`, so `dotnet test` over the solution, and
with it `verify` and `coverage`, leaves it out; it stays in `AppTemplate.sln`, so it builds and is
formatted with the rest. `test --no-integration` runs only the projects that set `IsTestProject`.
<!--#if (ci)-->
CI runs the task in the `end-to-end` job, once the Compose manifest is checked, and fails on a red
test.
<!--#endif-->

## Coverage

```bash
dotnet run Tools/Tasks.cs coverage
```

It runs the suite with coverage and compares the merged line coverage with the floor in
`coverage.minimum`. `Tools/CoverageGate.cs` merges the per-module Cobertura reports as a union over
(file, line), so an assembly two modules exercise counts once. Settings reach the platform through
`--coverage-settings`; `--settings` is accepted and ignored.

The floor is a tripwire: it catches an area shipped with no test, which no review of single tests
sees. It is measured, never invented: a Debug run with Docker present, both integration suites
contributing, a few points under the figure. Re-measure it when your own code lands, under the same
conditions. It is never raised to reward tests, and a drop is answered by naming the risk now
untested, not by a test written for the figure.

## House rules

A test that buys a guarantee must be able to fail: break the production code, watch the test go red,
restore it, and check the failure named what you meant.

| Rule | Example |
|---|---|
| A property of a population is one test that names each offender | `ErrorCodeTableTests` |
| An architecture rule proves its candidate set is not empty before asserting | `RuleAssertions.RequireTypes` |
| A population is measured from the tree, never typed as a number | the floors in `BackgroundWorkTests` |
| A limit is asserted against a literal once; every other test reads the constant | length and cap tests in the domain suite |
| A test pins what a rule leaves unchecked on purpose | validators of anonymous routes, which check presence only |
| A refusal test carries its control: the same call succeeding, or a read showing nothing changed | the ownership and refusal tests of the API suite |
| A response is read as raw text and parsed; two problem documents are compared without their `traceId` | `ApiJson` |
| A test reaches storage through a port or through SQL, never a persistence type | the integration tests' load helpers |
| A race is made a fact with `Rendezvous`, never hoped for with `Task.WhenAll` | concurrency tests in the integration suites |
| A guarantee the framework produces is proved through the framework | the polymorphic responses, read from the wire |
| A test double keeps the real behaviour a guarantee depends on | the aggregate builders under `TestDoubles` |
| A hand-written double replaces a substitute that would hide what is tested | the recording loggers |
| Rehydration is tested as an act of its own: it raises nothing and refuses an impossible row | the domain suite |
| Instrument tests assert with `ShouldContain` on a concurrent queue | the observability tests |
| A configuration test writes down the budget it asserts | the outbound HTTP policy tests |
| A value read from outside the repository is anchored to the type name | configuration section names |
| A presentation project grants its internals to its own unit tests only | one `InternalsVisibleTo` each |
<!--#if (localAuth)-->
| Anonymous routes are tested for sameness, not only refusal | `LoginUniformityTests` |
| A credential change is proved by replaying the token in circulation | `SecurityStampRotationTests` |
<!--#endif-->

Async calls pass `TestContext.Current.CancellationToken`; NSubstitute placeholders use
`Arg.Any<CancellationToken>()`.

## Sharp edges

- `Result<TValue>.Value` throws on a failure, so a pattern that reads it while matching throws: test
  `IsFailure` first.
- FluentValidation runs a property's later rules after `NotNull()` fails; a `Must` that dereferences
  needs `.Cascade(CascadeMode.Stop)`.
- `Arg.Is<T>` takes an expression tree, which refuses pattern matching (CS8122).
- The injectable clock moves neither token validation nor the account store, which read the system
  clock.
- The rate limiter's window runs on wall-clock time; `RateLimiting:Window` lets a test host widen it.
<!--#if (localAuth)-->
- The end-to-end suite calls the sign-in routes up to nine times, under the ten a minute
  `RateLimiting:AuthenticationPermitLimit` allows an address: a second run against a kept stack
  within the minute fails with `429`.
<!--#endif-->
- A folder named after the type it holds makes name lookup ambiguous (CS0118), and an `Options/` folder
  shadows `Options.Create`: use `new OptionsWrapper<T>(…)`.
<!--#if (localAuth)-->
- A passkey ceremony needs an `Origin` header, which TestServer does not send; `PasskeyTestSupport`
  adds it.
<!--#endif-->
- A unit-test project with no test exits with code 8; the domain and application projects ignore it
  only while they hold no test folder.
- `.cs` and `.md` files are UTF-8 with BOM, and a file without one fails the formatting gate.
- An unused `using` and a stale `<see cref="…"/>` fail the build, since every project generates its
  documentation file.

## Decisions

The test stack is a row of [ADR 0001](../decisions/0001-adopt-the-generated-baseline.md); the
strategy above is [ADR 0020](../decisions/0020-one-risk-one-type-of-test.md).
