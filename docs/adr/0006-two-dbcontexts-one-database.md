# 0006 — Two DbContexts on one database

Status: **Superseded by [0010](0010-one-persistence-project-one-dbcontext.md)**

> **Superseded, not deleted.** The reasoning below is still the best statement of *why* the
> identity boundary matters, and the "consequences" list is an accurate account of what two
> contexts cost. What changed is the mechanism: the boundary is now kept by EF not mapping the
> domain entities at all (see
> [0011](0011-persistence-models-separate-from-the-domain.md)), which is stronger than context
> separation, so the two contexts were paying for something they were no longer the only thing
> providing. Read this record for the problem; read
> [0010](0010-one-persistence-project-one-dbcontext.md) for the current answer.

## Context

ASP.NET Identity's persistence model is imposed by the framework:
`IdentityDbContext<TUser, TRole, TKey>` brings seven tables and their configuration, and
they are not domain entities in any useful sense.

The template previously had two contexts *and* two configuration keys —
`DefaultConnection` and `IdentityConnection` — which described two databases that were
always in practice the same one. Two names for one thing means they can be configured
inconsistently, and nothing notices until runtime.

## Decision

**Two DbContexts, one database, two schemas, one connection string.**

| Context | Schema | Migrations history |
|---|---|---|
| `AppDbContext` | default (`public`) | `public.__EFMigrationsHistory` |
| `AppIdentityDbContext` | `identity` | `identity.__EFMigrationsHistory` |

Both resolve from the single `ConnectionStrings:Default`. `AppIdentityDbContext` calls
`HasDefaultSchema("identity")`, and both the DI registration and the design-time factory
place the migrations history table in the owning schema.

## Consequences

- **The identity boundary is a compile-time fact.** A domain entity cannot acquire a
  navigation property to `AppUser`, because `AppUser` is not in its model.
  `TodoList.OwnerId` is a bare `Guid` with no foreign key to the identity store — which is
  exactly what keeps replacing the identity provider possible.
- Each context migrates independently. An Identity framework upgrade that changes its
  schema does not touch the domain's migration history, and vice versa.
- One connection string, one backup, one set of credentials, one `pg_dump`.
- **There is no transaction spanning the two contexts.** Registering a user and creating
  domain rows for them are two commits, and the second can fail after the first
  succeeded. The accepted answer is idempotent, retryable operations — not a distributed
  transaction that would only appear to work.
- `IUnitOfWork` covers `AppDbContext` only. The identity vertical saves through its own
  context (`UserManager`, `RefreshTokenService`), so the "use case owns the commit" rule
  has a documented exception. Registration is explicitly non-atomic for this reason: the
  account is committed before the confirmation email is sent, `RegisterResponse` reports
  `ConfirmationEmailSent = false` when delivery fails, and a resend endpoint exists so the
  user is not stranded. Verified by request against a running instance.
- Every `dotnet ef` command needs `--context`. Two history tables means two answers to
  "is the schema current", and a deployment step must apply both.
- Cross-schema queries are possible in raw SQL but deliberately not done in EF: joining
  `public.TodoLists` to `identity.User` in a projection would reintroduce the coupling the
  split exists to prevent.

## Alternatives rejected

- **One context for everything.** Simplest, and it puts framework-shaped Identity
  entities in the same model as the domain aggregates. The first `TodoList.Owner`
  navigation property is then one autocomplete away, and after that the identity provider
  is load-bearing for the domain.
- **Two databases** (what the old configuration keys implied). Two connection strings, two
  backup schedules, two failure domains, and still no cross-database transaction. All the
  cost of separation with none of the convenience of one instance.
- **One context, two schemas.** Gets the table separation without the model separation —
  which is the part that matters. A single `DbContext` means a single model, and a single
  model is where the unwanted navigation property becomes possible.
- **A separate identity service over HTTP.** The right answer at a certain scale, and an
  enormous amount of operational machinery for a template. Splitting later is possible
  precisely because there is no foreign key across the boundary today.

## Revisit when

Identity needs to scale or be secured independently of the application data — different
credentials, different backup retention, different blast radius. Then promote the schema
to its own database; the absence of cross-schema foreign keys is what makes that a
configuration change rather than a rewrite.
