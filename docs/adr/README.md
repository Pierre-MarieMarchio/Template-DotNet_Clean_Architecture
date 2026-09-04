# Architecture decision records

One file per decision that a reader could reasonably have made differently. Each
records the context, the decision, what it costs, and what was rejected — because the
rejected option is usually the useful part.

These are records, not policy. If one of them is wrong for your project, supersede it
with a new numbered record rather than editing history.

| # | Title | Status |
|---|---|---|
| [0001](0001-net10-and-postgresql.md) | .NET 10 and PostgreSQL | Accepted |
| [0002](0002-no-mediatr-no-cqrs-ceremony.md) | No MediatR, no CQRS ceremony | Accepted |
| [0003](0003-aggregate-oriented-repository.md) | Aggregate-oriented repository instead of a generic one | Accepted |
| [0004](0004-result-as-the-failure-channel.md) | `Result` as the failure channel for expected outcomes | Accepted |
| [0005](0005-opaque-rotating-refresh-tokens.md) | Opaque rotating refresh tokens, not JWTs | Accepted |
| [0006](0006-two-dbcontexts-one-database.md) | Two DbContexts on one database | Superseded by 0010 |
| [0007](0007-module-per-capability-infrastructure.md) | Module-per-capability infrastructure, with no per-technology split | Accepted (amended by 0010) |
| [0008](0008-default-deny-authorisation.md) | Default-deny authorisation | Accepted |
| [0009](0009-no-migrations-at-startup-in-production.md) | Migrations are not applied at startup outside Development | Accepted |
| [0010](0010-one-persistence-project-one-dbcontext.md) | One persistence project, one DbContext | Accepted |
| [0011](0011-persistence-models-separate-from-the-domain.md) | EF Core maps persistence models, not the domain entities | Accepted |
| [0012](0012-hsts-is-owned-by-the-ingress.md) | HSTS is owned by the ingress, not by the application | Accepted |
| [0013](0013-if-match-is-optional-by-default.md) | `If-Match` is required by configuration, not by default | Accepted |
| [0014](0014-packaging-as-a-dotnet-new-template.md) | Packaging as a `dotnet new` template | Accepted |

## Format

Each record has the same five headings:

- **Context** — the situation, including what the template did before.
- **Decision** — what was chosen, in the present tense.
- **Consequences** — what follows, including the bad parts.
- **Alternatives rejected** — the options considered, and why they lost.
- **Revisit when** — the observation that would make this decision wrong.

Keep them short. A record nobody rereads has no value.
