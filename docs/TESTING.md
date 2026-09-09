# Testing

**xunit.v3 + Shouldly + NSubstitute** — not FluentAssertions, whose licence changed — with
`NetArchTest.Rules` for the dependency-direction rules and `Testcontainers.PostgreSql` for the
integration suites. Test projects live under `Tests/`, mirroring `Src/` one directory for one
directory.

- [Running it](#running-it)
- [With coverage](#with-coverage)
- [House rules](#house-rules)
- [What a derived project inherits](#what-a-derived-project-inherits)
- [The load smoke test](#the-load-smoke-test)
- [Coverage and the architecture tests](#coverage-and-the-architecture-tests)
- [Sharp edges](#sharp-edges)

## Running it

```bash
dotnet test AppTemplate.sln                       # everything
dotnet run Tools/Tasks.cs test                    # the same, through the launcher
dotnet run Tools/Tasks.cs test --no-integration   # skips the Testcontainers suites
dotnet test Tests/Architecture                    # the architecture rules alone
```

That is one invocation for the whole suite, and one process tree: `global.json` names
**Microsoft.Testing.Platform** as the test runner, so each test project builds as a console
application hosting its own runner and the platform runs them in parallel.

**Integration tests start a real PostgreSQL in Docker, so Docker must be running.** CI runs on
`ubuntu-latest`, which provides a daemon; the hosted macOS and Windows runners do not.

Every test project is listed in `AppTemplate.sln`, and it has to stay that way: the platform runs
the test modules the solution names, so a project on disk but absent from it is never run and
nothing in the run says so. CI asserts against exactly that. A new test project needs
`<IsTestProject>true</IsTestProject>` and an entry in the solution — the first is what makes
`Directory.Build.targets` mark it executable, without which xunit.v3 refuses to build it, and the
second is what makes it run at all. It needs no runner package.

> `Directory.Build.targets` exists for that one line. `Directory.Build.props` is imported before a
> project's own body, so `IsTestProject` is still empty there and the condition never matches.

## With coverage

```bash
dotnet run Tools/Tasks.cs coverage    # line coverage against the floor in coverage.minimum
```

What CI runs, and the switches are the platform's own rather than VSTest's:

```bash
dotnet test AppTemplate.sln \
  --configuration Release \
  --results-directory TestResults \
  --report-xunit-trx \
  --coverage \
  --coverage-settings coverage.runsettings \
  --coverage-output-format cobertura \
  --minimum-expected-tests 3000
```

The resemblance to VSTest is a trap: there is no `--logger` and no `--collect`, and `--settings` is
accepted and then ignored, so coverage settings have to arrive through `--coverage-settings` or
every exclusion in that file goes unapplied. One Cobertura report is written per test module;
`Tools/CoverageGate.cs` merges them as a union over (source file, line) before comparing against
the floor.

`--minimum-expected-tests` is the platform's own answer to a run that is green because it executed
nothing: it fails the run, with exit code 5, rather than reporting success over zero tests.

`coverage.minimum` holds the floor and the measurements it was set from. Read that file before
moving the number — the figure depends on the build configuration and on whether Docker was
present, and two rows measured differently are not comparable.

## House rules

- `TestContext.Current.CancellationToken` in async calls. xUnit1051 is an error here.
- `Arg.Any<CancellationToken>()` for NSubstitute placeholders, never `default`.
- The tree mirrors `Src/` exactly, down to `Features/<F>/`.

**A test that buys a guarantee must be able to fail.** For anything about security or correctness:
break the production code, *watch the test go red*, restore it, and check the failure named what you
thought it named. A green test you never saw fail is not evidence. Rules follow from experience in
this repository:

- An architecture rule can pass **vacuously** on an empty candidate set. Assert the set is non-empty
  *before* asserting the condition. `AggregateRoots_AreSealed` once matched zero types and passed.
- A test project missing from `AppTemplate.sln` makes `dotnet test AppTemplate.sln` green with zero tests. CI asserts
  every project under `Tests/` is in the solution, and that the run executed more than zero tests.
- **A test that picks its own static type does not test the real path.** A unit test asserting the
  discriminator of a polymorphic response passed while the API served a body without one, because it
  called `JsonSerializer.Serialize<TBase>(…)` and named the base itself. MVC does not: `Ok(value)`
  leaves `DeclaredType` null and the formatter serialises the runtime type. When a guarantee depends
  on the framework, prove it through the framework — read the raw response.
- **A comment claiming a guarantee is covered elsewhere is a claim to check, not evidence.** Several
  have been wrong here: an ETag asserted to be replayed that was not, a cross-reference naming the
  wrong layer, a formatting gate believed to catch unused usings that did not.
- **Some defects only an end-to-end test can see.** An exhaustive `switch` over an event enum threw
  on a newly added member and turned the first real call into a 500; every unit test around it had
  substituted that collaborator away. Where a component is substituted everywhere, something has to
  exercise the real one.
- **Never compare two problem documents whole.** They carry a `traceId`, which identifies the
  request rather than its outcome, so two requests always differ — an assertion that they are equal
  is asserting that two requests are the same one. Compare everything else.

## What a derived project inherits

A project generated from this template receives the whole of `Tests/`, fixtures included. That is
the delivery mechanism rather than a gap, and it is why there is no test-kit project to reference:
a kit could only hold the fixtures that name nothing of this product, which is the easy fifth of
them, and the two a derived project actually wants could not go in it.

**Seven fixtures name no product type and can be leaned on as they are** — 356 lines between them.
Under `Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/`: `ApiJson`, the
serialiser settings a raw response is read with; `CapturedLogs`, which makes an assertion about a
log entry possible; `SecurityHeaderAssertions`; and `TestClientAddressStartupFilter`, which is what
lets a test choose the address the rate limiter partitions on. Under the identity project's
`Fixtures/`: `DatabaseReadiness`, `Rendezvous`, which is how two concurrent contexts are made to
race deliberately, and `AnonymousAuditActor`.

**Two name the product and have to be edited, and they are the two that matter.** `ApiFactory`
names ten product namespaces and `IntegrationTestBase` eight: the modules a test host replaces, the
options it overrides, the use cases it reaches past HTTP. Nothing can be done about that — a fixture
that boots this application knows this application — so treat them as your own code from the first
commit, and expect the edit to be a real one when a feature is removed or a module swapped.
[`REMOVING-THE-EXAMPLE-FEATURES.md`](REMOVING-THE-EXAMPLE-FEATURES.md) names what each removal costs in them.

The one duplication actually present in the fixtures is the PostgreSQL container builder: five
lines, three times, differing only in the database name. That is under the bar this file sets for
an extraction, and it stays.

## The load smoke test

`Tests/Load/smoke.js` is a k6 script, and CI runs it **non-blocking**. That is the design, not a
concession: a hosted runner's timings are not a property of this code, so a threshold tight enough
to mean anything on real hardware would be noise there — and a red build nobody can act on is a red
build people learn to ignore. Its thresholds describe a **broken** system (requests failing,
ten-second responses), never a slow one, and a `429` counts as a pass, because the rate limiter
doing its job under load is the correct outcome and a test that punished it would push someone to
loosen the limit to get a green.

It exists because every timeout, pool size and rate limit in this template is chosen by reasoning,
and no other test in the suite observes the result under concurrency. Run it against a stack of
your own:

```bash
docker compose up -d --build
k6 run -e BASE_URL=http://localhost:8080 Tests/Load/smoke.js
```

## Coverage and the architecture tests

NetArchTest resolves each type through `Type.GetType(name, throwOnError: true)`, and that
resolution is sensitive to how the assembly it is looking in was instrumented — it fails outright
against a Coverlet-instrumented one. The collector the platform uses does not break it: all 131
rules in `AppTemplate.Architecture.Tests` pass with the twelve product assemblies that project
references instrumented, which is why coverage is collected over the whole solution in one
invocation.

If you change collector or its settings, re-run that suite under coverage before trusting a green
elsewhere. It is the one project whose failure mode is a thrown resolution rather than a wrong
number.

## Sharp edges

Each of these has been paid for at least once in this repository.

- **`Result<TValue>.Value` throws on a failure.** So `is { IsSuccess: true, Value: var x }` evaluates
  the getter *while* matching and throws instead of not matching. Test `IsFailure` first.
- **FluentValidation runs the remaining rules for a property even after `NotNull()` failed.** A
  `Must` that dereferences needs `.Cascade(CascadeMode.Stop)` ahead of it.
- **NSubstitute's `Arg.Is<T>` takes an expression tree**, which rejects pattern matching (CS8122).
  Compare a record by value instead of by predicate.
- **The injectable clock controls neither JWT validation nor ASP.NET Identity**, both of which read
  `TimeProvider.System`. Moving it forward and then signing in mints a token whose `nbf` is in the
  future, refused as `IDX10222` — not an expired one. It has no effect on lockout end dates or on
  confirmation and reset token lifetimes either.
- **The rate limiter's window advances on wall-clock time** and exposes no injectable clock;
  `AutoReplenishment = false` does not change that. The window itself is replaceable
  (`RateLimiterWindow`) so a test host can widen it.
- **Never name a folder after the type it contains.** A namespace and a type sharing a name make
  name resolution ambiguous for consumers (CS0118), because lookup walks the enclosing namespaces.
- **An `Options/` folder shadows `Microsoft.Extensions.Options.Options`** for every file under the
  namespace that encloses it — the same enclosing-namespace lookup as above, one word at a time. So
  inside the identity and storage modules and their mirrors, `Options.Create(…)` does not compile:
  use `new OptionsWrapper<T>(…)`, or qualify it in full. Only the bare name is affected;
  `IOptions<T>` and `IValidateOptions<T>` resolve as usual.
- **`.cs` files are UTF-8 *with* BOM** (`.editorconfig`), and a file written without one fails the
  formatting gate. Detecting a BOM by piping bytes through `grep` does not work reliably — it will
  happily add a second one.
- **A stale `<see cref="…"/>` and an unused `using` both fail the build**, because
  `GenerateDocumentationFile` is on with CS1591/CS1573 suppressed. Documentation stays optional;
  what is written has to be true.

