# 0007 — Module-per-capability infrastructure, with no per-technology split

Status: Accepted — **amended by [0010](0010-one-persistence-project-one-dbcontext.md)**

> **Amendment.** The rule below still holds for every module except one.
> `AppTemplate.Infrastructure.TodoLists` is gone: all persistence — the to-do list feature's *and* the
> identity store — now lives in `AppTemplate.Infrastructure.Persistence`, which therefore holds more
> than one capability. It is partitioned internally instead (`Common/` plus
> `Features/<Feature>/`), and an architecture test asserts that nothing among the shared
> mechanisms names a feature. The four modules under `Src/Infrastructure/` are
> `AppTemplate.Infrastructure.Persistence`, `.Identity`, `.Email` and `.InMemory`. The two rules about
> reference direction are unchanged and still enforced.

## Context

Infrastructure was one project, `AppTemplate.Infrastructure`, plus `AppTemplate.Infrastructure.Identity`
bolted alongside it. Everything database-shaped landed in the first: the `DbContext`, the
interceptors, the repositories, the queries, the domain-event handlers, and a
`ServiceRegister` that registered all of it in one method. Adding a capability meant
editing that method; understanding one capability meant reading past the others.

The registration in the version this replaces was worse than merely large: it enumerated
two assemblies and paired interfaces with implementations by **matching type names**, so
a rename produced a container that started fine and threw on first use.

## Decision

**One project per capability, one DI extension method per project, and — where the
capability needs storage — one `DbContext` in one schema.**

| Module | Registers | Storage |
|---|---|---|
| `AppTemplate.Infrastructure.Persistence` | `AppDbContext`, interceptors, `IUnitOfWork`, `ITodoListRepository`, `ITodoListQueries`, `IRefreshTokenStore`, `IIdentitySeeder` | `AppDbContext` → `todo` + `identity` |
| `AppTemplate.Infrastructure.Identity` | the authentication capability ports, Identity, JWT bearer, refresh tokens | — (uses the shared context) |
| `AppTemplate.Infrastructure.Email` | `IEmailSender`, email options | — |
| `AppTemplate.Infrastructure.InMemory` | in-memory port implementations for tests and demos | — |

*(Table as amended by [0010](0010-one-persistence-project-one-dbcontext.md). The original
listed a separate `AppTemplate.Infrastructure.TodoLists` with its own `DbContext`.)*

Two rules the compiler cannot state, so they are stated here:

- **Modules reference `Persistence`; `Persistence` never references a module.**
- **Modules do not reference each other.** Anything two modules both need belongs in
  Application (as a port) or in Persistence (as mechanics).

Every registration is explicit. Nothing is discovered by scanning assemblies or matching
names.

## And deliberately: no `.Core` / `.<Technology>` sub-split

The tempting next step is `AppTemplate.Infrastructure.Persistence.Core` + `.PostgreSql`, or
`AppTemplate.Infrastructure.Email.Core` + `.MailKit`. This template does not do that.

- There is **one database** and **one SMTP client**. An abstraction with exactly one
  implementation is not an abstraction; it is a second file to keep in sync and a second
  hop for the reader.
- The seam that actually buys portability already exists, one layer further in: the
  Application-side port. `IEmailSender` is what a second transport would implement.
  Splitting *inside* Infrastructure adds an interface below the interface that matters.
- The cost is immediate and permanent: twice the projects, twice the DI wiring, and a
  reader who must open two files to answer "what does this do".
- A speculative split is a guess at the shape of the second implementation. Doing it when
  the second implementation actually arrives means designing against something real, and
  the move is mechanical either way.

## Consequences

- `AppTemplate.Api`'s composition root becomes a list of `AddXInfrastructure(configuration)` calls.
  Adding a capability adds one line; removing one deletes a project and a line.
- The project graph enforces most of the architecture. A module cannot accidentally depend
  on another module's internals, because it cannot see them.
- ~~Each module's DbContext, schema and migrations are its own.~~ Reversed by
  [0010](0010-one-persistence-project-one-dbcontext.md): there is one context and one
  migrations history, and the features separate themselves by schema.
- More `.csproj` files, a longer `AppTemplate.sln`, and a marginally slower cold build. Accepted.
- ~~`AppTemplate.Infrastructure.Persistence` is a project with no capability of its own.~~ As of
  [0010](0010-one-persistence-project-one-dbcontext.md) it owns every capability's storage as
  well as the commit boundary, the interceptors and the model, and is partitioned by feature
  internally.
- **The Dockerfile's `COPY` list names each `.csproj` explicitly and must be updated when
  a module is added or renamed**, or restore inside the image fails on a missing project
  while the local build stays green. The `docker` job in CI is the guard.
- Test projects mirror the tree one-for-one, so a module's tests are as isolated as the
  module.

## Alternatives rejected

- **One Infrastructure project** (what was there). Nothing prevents a repository from
  reaching into the identity store, and the DI method grows without bound.
- **Split by technical layer** (`.Repositories`, `.Configurations`, `.Interceptors`).
  Groups by what things *are* rather than what they are *for*, so understanding one
  feature means visiting every project. This is the layout that makes deleting a feature
  hard.
- **`.Core` + `.<Technology>` per capability.** Argued above. It is the right shape once a
  second implementation genuinely exists.
- **Assembly scanning / convention-based registration** (what was there). A rename
  silently unwires the container and fails at the first request. Explicit registration
  turns that into a compile error.
- **Source-generated DI or a container with auto-registration.** Solves the typing, keeps
  the "what is actually registered" question unanswerable from reading the code.

## Revisit when

A second database engine, a second mail transport, or a second identity provider actually
arrives — then split that one module by technology, with the real second implementation in
hand, and leave the others alone.
