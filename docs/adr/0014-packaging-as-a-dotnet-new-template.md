# 0014 — Packaging as a `dotnet new` template

Status: Accepted

## Context

The repository's own project prefix was `CA` — `CA.Domain`, `CA.Application`,
`CA.Api`, and so on. `dotnet new`'s `sourceName` mechanism replaces that token by
literal substring match wherever it appears — file names, folder names,
namespaces, `.sln`/`.csproj` contents. `CA` collides with the analyzer rule-ID
prefix `CAxxxx`: a `sourceName` of `CA` would rewrite `dotnet_diagnostic.CA1707`
(a real, justified suppression in `.editorconfig`) into
`dotnet_diagnostic.<NewName>707`, silently destroying the suppression in every
generated project, with a build failure that points nowhere near the cause. The
prefix was renamed to `AppTemplate` first — a token that cannot collide with any
`CAxxxx` id now or when a future suppression is added — and only then packaged.

Two further problems are specific to templating, not renaming: the repository
carries lowercase, hyphen- and underscore-separated identifiers (`docker-compose.yml`'s
project name and image tag, `.env`'s `POSTGRES_DB`/`POSTGRES_USER`) that a
PascalCase `sourceName` substitution does not reach on its own; and the sample
`TodoLists` feature is used as the test subject inside several cross-cutting
integration tests (ownership isolation, rate limiting, conditional requests,
auditing), not only inside its own vertical.

## Decision

**`sourceName` is `AppTemplate`.** `dotnet new cleanarch-webapi -n Acme.Order`
yields `Acme.Order.Domain`, `Acme.Order.Api`, an Acme.Order.sln solution file, and
namespaces rooted at `Acme.Order.*`.

**The Compose project name and image tag are a derived symbol, not hand-copied
text.** A generated `generator: casing` symbol lowercases the chosen name, and a
`generator: regex` symbol turns `.`/whitespace/`_` into `-`; that kebab form
replaces the literal `app-template` wherever it occurs — including as a substring
of `app-template-api` — so two generated projects never share a Compose project
name or an image tag.

**The PostgreSQL database name and user are generic, not derived.**
`POSTGRES_DB=appdb` and `POSTGRES_USER=appuser` stay the same for every generated
project. A database credential is an operational detail an operator rotates and
scopes independently of the application's identity; deriving it from the project
name would produce collisions of a different kind (a name like `Acme.Order`
kebab-cased and suffixed becomes noisy — `acme-order-template` — for no benefit,
since nothing reads that name back out of the project's own identity) and would
tie a security-relevant default to a string that is, by design, meant to change
per environment anyway.

**Every project GUID is regenerated per generation** (`template.json`'s `guids`),
so two generated solutions never collide if a GUID leaks into shared state (a
NuGet cache key, a CI cache, a solution opened side by side in one IDE instance).
The two Visual Studio *type* GUIDs (C# project, solution folder) are left alone —
they are constants, not instance identifiers.

**There is no generator switch to exclude the `TodoLists` sample.** See
"Alternatives rejected".

## Consequences

- A generated project whose name does not start with `A` (as `AppTemplate` does)
  fails `dotnet format --verify-no-changes` until `dotnet format` runs once,
  because sorted `using` directives depend on the chosen name's alphabetical
  position relative to third-party usings (`Xunit`, `Microsoft.*`, …). This is
  inherent to literal-substitution templating — no fixed ordering in the source is
  correct for every possible generated name — and is called out once, in
  `README.md`'s template-usage section, rather than worked around.
- `.github/workflows/ci.yml`'s `template` job installs the template, generates a
  project under a name unrelated to `AppTemplate`, and builds and tests it on
  every push. Without this, the template can rot silently the first time a file
  moves and nobody notices until someone tries to generate from it.
- The sample stays in every generated project. Anyone who does not want it does
  the deletion by hand, guided by `docs/ADDING-A-FEATURE.md`'s last section.

## Alternatives rejected

- **A `--exclude-sample` switch removing `Features/TodoLists/` everywhere it
  appears.** Tried first. The `TodoLists` folders themselves are one exclusion
  glob each — the easy 80%. The remaining 20% is not: `ServiceRegistration.cs` and
  `PersistenceModule.cs` wire the feature by hand alongside code every generated
  project needs regardless, the initial migration mixes the identity schema and
  the `TodoLists` schema in one file, and — the part that made this untenable —
  roughly a dozen integration tests under `Tests/Integration/AppTemplate.Api.IntegrationTests/Security/` use a
  `TodoLists` endpoint as an arbitrary authenticated resource to exercise a
  cross-cutting concern that has nothing to do with to-do lists. Making the switch
  produce a project that both builds *and* keeps that coverage would mean
  rewriting those tests against a synthetic resource, which is a bigger and
  riskier change than "delete a feature," attempted under this same task. Shipping
  the switch anyway, either broken or with silently reduced test coverage in the
  excluded position, would have failed this decision's own standard: a template
  that generates a broken (or quietly worse) project is worse than a repository to
  clone.
- **Deriving `POSTGRES_DB`/`POSTGRES_USER` from the project name.** Rejected —
  see "Decision" above.
- **Leaving the Compose project name and image tag as the literal
  `app-template`/`app-template-api`.** Works for exactly one generated project at a
  time; a second one on the same Docker host collides on the Compose project name
  and the image tag. Rejected in favour of the derived kebab-case symbol.

## Revisit when

Someone needs the sample gone badly enough to fund rewriting the integration
tests it currently powers — at that point, `--exclude-sample` becomes a real
option instead of a shortcut, and this record should be superseded.
