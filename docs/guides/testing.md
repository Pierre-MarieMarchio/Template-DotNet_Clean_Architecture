# Testing

## The suites

`Tests/` mirrors `Src/` project for project, down to `Features/<Feature>/`. The stack is xUnit v3 on
Microsoft.Testing.Platform, Shouldly, NSubstitute, NetArchTest for the architecture rules and
Testcontainers for PostgreSQL.

| Suite | Proves | Needs |
|---|---|---|
| Unit tests, one project per source project | each type's behaviour, against doubles | nothing |
| `Tests/Architecture/AppTemplate.Architecture.Tests` | the dependency rule, the folder vocabulary, the closed lists, the container compositions | nothing |
| `Tests/Integration/AppTemplate.Api.IntegrationTests` | the HTTP surface end to end, against a real PostgreSQL | Docker |
<!--#if (localAuth)-->
| `Tests/Integration/AppTemplate.Infrastructure.Auth.IntegrationTests` | the identity round trips a fake store cannot prove, such as one refresh winning a race | Docker |
<!--#endif-->
| `Tests/Load/smoke.js` | nothing comes apart under concurrency; a k6 script, never a gate | k6 and a running stack |

Each integration project starts one PostgreSQL container and one host for the whole suite, and every
class joins one collection, so tests run one after another. `IntegrationTestBase` resets the rows, the
mailbox, the captured log and the clock before each test; `TestDatabase` truncates the schemas it
lists, and a schema it does not list leaks rows into the next test.
<!--#if (files)-->
The `Storage/` suite starts the object store the stack runs, at the same digest.
<!--#endif-->

## Running them

```bash
dotnet run Tools/Tasks.cs test                    # the solution
dotnet run Tools/Tasks.cs test --no-integration   # the projects that need no Docker
dotnet test Tests/Architecture                    # the architecture rules alone
```

`global.json` names Microsoft.Testing.Platform as the runner: each test project is a console
application, and the platform runs the modules the solution lists. A test project absent from
`AppTemplate.sln` never runs, so a new one needs `<IsTestProject>true</IsTestProject>` and an entry in
the solution; CI fails when one is missing.

A run killed outright leaves its containers behind, because the resource reaper is off
(`ResourceReaperSwitch`). Remove them by label:

```bash
docker rm -f $(docker ps -aq --filter label=org.testcontainers=true)
```

## Coverage

```bash
dotnet run Tools/Tasks.cs coverage
```

It runs the suite with coverage and compares the merged line coverage with the floor in
`coverage.minimum`. `Tools/CoverageGate.cs` merges the per-module Cobertura reports as a union over
(file, line), so an assembly two modules exercise counts once. Settings reach the platform through
`--coverage-settings`; `--settings` is accepted and ignored.

The floor is measured, never invented: a Debug run with Docker present, both integration suites
contributing, a few points under the figure. Re-measure it when your own code lands, with the same
conditions, and state the new number in `coverage.minimum`.

## House rules

A test that buys a guarantee must be able to fail: break the production code, watch the test go red,
restore it, and check the failure named what you meant.

| Rule | Example |
|---|---|
| An architecture rule proves its candidate set is not empty before asserting | `RuleAssertions.RequireTypes` |
| A population is measured from the tree, never typed as a number | the floors in `BackgroundWorkTests` |
| A limit is asserted against a literal once; every other test reads the constant | length and cap tests in the domain suite |
| A test pins what a rule leaves unchecked on purpose | validators of anonymous routes, which check presence only |
| A refusal test carries its control: the same call succeeding, or a read showing nothing changed | the ownership and refusal tests of the API suite |
| A test over a whole surface enumerates it by reflection and asserts the count | `IdempotentActionsAreAlwaysPostTests` |
| A response is read as raw text and parsed; two problem documents are compared without their `traceId` | `ApiJson` |
| A test reaches storage through a port or through SQL, never a persistence type | the integration tests' load helpers |
| A race is made a fact with `Rendezvous`, never hoped for with `Task.WhenAll` | concurrency tests in both integration suites |
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

The test stack is a row of [ADR 0001](../decisions/0001-adopt-the-generated-baseline.md).
